using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Auth.Application.Configuration;
using Auth.Application.DTOs;
using Auth.Application.Features.Platform.SetPlatformFont;
using Auth.Application.Interfaces;
using Auth.Domain.Constants;
using Auth.Domain.Entities;
using Auth.Domain.Enums;
using Auth.Domain.Errors;
using Auth.Domain.Events;
using Auth.Domain.Interfaces.Repositories;
using Auth.Domain.ValueObjects;
using Auth.Infrastructure.Fonts;
using Auth.Infrastructure.Services;
using Auth_API.Modules.AuditLog.EventHandlers;
using Auth_API.Modules.Media;
using Auth_API.Tests.Infrastructure;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using AuditLogEntity = Auth.Domain.Entities.AuditLog;

namespace Auth_API.Tests.Branding;

/// <summary>
/// The platform's own fonts (UI batch P4, item 7; owner decision D7): one WOFF2 file of at
/// most 2 MiB per role (heading, body), kept byte for byte after a structural check, stored in
/// the uploads volume under a content-hash name, served cross-origin, and deleted once no role
/// uses it.
/// </summary>
/// <remarks>
/// Deliberate breaks, each turning tests here red: drop the Brotli check in
/// <c>Woff2Font.Read</c> (the garbage-body case); drop the length check (the truncated case);
/// take <c>Access-Control-Allow-Origin</c> off fonts in <c>UploadsStaticFiles</c>; drop the
/// <c>UsesFontFile</c> guard before deleting a replaced file (the shared-file case); drop the
/// rollback in <c>SetPlatformFontCommandHandler</c> (the failed-save case).
/// </remarks>
public class PlatformFontsTests : IDisposable
{
    private static readonly Guid Admin = Guid.NewGuid();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fonts-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // --- a WOFF2 file, built here ---

    /// <summary>
    /// A minimal WOFF2 (head, cmap, name), every table untransformed, compressed with Brotli as
    /// the format requires. <paramref name="family"/> goes into the name table as name ID 1 in
    /// Windows Unicode English.
    /// </summary>
    internal static byte[] Woff2(
        string? family = "Test Sans",
        uint flavor = 0x00010000,
        bool withCmap = true,
        Func<byte[], byte[]>? compress = null,
        int declaredLengthDelta = 0)
    {
        var tables = new List<(byte Index, byte[] Data)> { (1, new byte[54]) };
        if (withCmap)
        {
            tables.Add((0, new byte[4]));
        }

        tables.Add((5, NameTable(family)));

        var directory = new MemoryStream();
        foreach (var (index, data) in tables)
        {
            directory.WriteByte(index); // version 0: no transform for these tables
            WriteBase128(directory, (uint)data.Length);
        }

        var raw = tables.SelectMany(table => table.Data).ToArray();
        var compressed = (compress ?? Brotli)(raw);
        var length = 48 + (int)directory.Length + compressed.Length;

        var file = new byte[length];
        BinaryPrimitives.WriteUInt32BigEndian(file, 0x774F4632);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), flavor);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(8), (uint)(length + declaredLengthDelta));
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(12), (ushort)tables.Count);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(16), (uint)(12 + (16 * tables.Count) + raw.Length));
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(20), (uint)compressed.Length);
        BinaryPrimitives.WriteUInt16BigEndian(file.AsSpan(24), 1);
        directory.ToArray().CopyTo(file, 48);
        compressed.CopyTo(file, 48 + (int)directory.Length);
        return file;
    }

    private static byte[] Brotli(byte[] data)
    {
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] NameTable(string? family)
    {
        var text = family is null ? [] : Encoding.BigEndianUnicode.GetBytes(family);
        var count = family is null ? 0 : 1;
        var table = new byte[6 + (12 * count) + text.Length];
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(2), (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(4), (ushort)(6 + (12 * count)));
        if (count == 1)
        {
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6), 3);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(8), 1);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(10), 0x409);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(12), 1);
            BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(14), (ushort)text.Length);
            text.CopyTo(table, 18);
        }

        return table;
    }

    private static void WriteBase128(Stream stream, uint value)
    {
        var bytes = new List<byte>();
        do
        {
            bytes.Insert(0, (byte)(value & 0x7F));
            value >>= 7;
        }
        while (value != 0);

        for (var i = 0; i < bytes.Count - 1; i++)
        {
            bytes[i] |= 0x80;
        }

        stream.Write(bytes.ToArray());
    }

    // --- the reader: what is a WOFF2 font ---

    [Fact]
    public void AWholeWoff2Font_IsRead_WithTheFamilyItDeclares()
    {
        Woff2Font.Read(Woff2("Plex Test")).Should().NotBeNull()
            .And.Subject.As<Woff2Font.Info>().FamilyName.Should().Be("Plex Test");
    }

    [Fact]
    public void AFontWithNoFamilyName_IsStillAFont()
    {
        Woff2Font.Read(Woff2(family: null))!.FamilyName.Should().BeNull();
    }

    public static TheoryData<string, byte[]> NotWoff2Files() => new()
    {
        { "a PNG renamed .woff2", [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[64]] },
        { "a WOFF 1.0", [.. "wOFF"u8, 0, 1, 0, 0, .. new byte[64]] },
        { "a TrueType file", [0, 1, 0, 0, 0, 3, .. new byte[64]] },
        { "an HTML page", [.. "<!doctype html><script>alert(1)</script>"u8, .. new byte[16]] },
        { "an SVG", [.. "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>"u8] },
        { "the signature alone", [.. "wOF2"u8] },
        { "nothing", [] },
    };

    [Theory]
    [MemberData(nameof(NotWoff2Files))]
    public void AFileThatIsNotWoff2_IsRefused(string what, byte[] file)
    {
        Woff2Font.Read(file).Should().BeNull(what);
    }

    [Fact]
    public void TheWoff2Signature_WithAGarbageBody_IsRefused()
    {
        var file = Woff2(compress: data => Enumerable.Range(0, 200).Select(i => (byte)(i * 37)).ToArray());

        Woff2Font.Read(file).Should().BeNull("the data does not decompress to the declared tables");
    }

    [Fact]
    public void ATruncatedFile_IsRefused()
    {
        var file = Woff2();

        Woff2Font.Read(file.AsSpan(0, file.Length - 10)).Should().BeNull();
    }

    [Fact]
    public void ADeclaredLengthThatIsNotTheFiles_IsRefused()
    {
        Woff2Font.Read(Woff2(declaredLengthDelta: 1)).Should().BeNull();
    }

    [Fact]
    public void DataThatExpandsPastTheDeclaredTables_IsRefused()
    {
        Woff2Font.Read(Woff2(compress: data => Brotli([.. data, .. new byte[100]]))).Should().BeNull();
    }

    [Fact]
    public void DataShorterThanTheDeclaredTables_IsRefused()
    {
        Woff2Font.Read(Woff2(compress: data => Brotli(data[..^4]))).Should().BeNull();
    }

    [Fact]
    public void AFontCollection_IsRefused()
    {
        Woff2Font.Read(Woff2(flavor: 0x74746366)).Should().BeNull("a page names one font in an @font-face, not a collection");
    }

    [Fact]
    public void AFontWithoutACharacterMap_IsRefused()
    {
        Woff2Font.Read(Woff2(withCmap: false)).Should().BeNull();
    }

    [Fact]
    public void TablesDeclaredLargerThanTheBound_AreRefusedBeforeAnythingIsAllocated()
    {
        var file = Woff2();
        // The head table's length (the first directory entry): 0x8F 0xFF 0xFF 0xFF 0x7F is 2^32 - 1.
        var huge = new byte[] { 0x01, 0x8F, 0xFF, 0xFF, 0xFF, 0x7F };
        var rebuilt = file[..48].Concat(huge).Concat(file[(48 + 2)..]).ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(rebuilt.AsSpan(8), (uint)rebuilt.Length);

        Woff2Font.Read(rebuilt).Should().BeNull();
    }

    // --- the store ---

    private FileSystemFontStorageService Store()
    {
        var monitor = new Mock<IOptionsMonitor<ImageStorageSettings>>();
        monitor.Setup(m => m.CurrentValue).Returns(new ImageStorageSettings { PhysicalPath = _root });
        return new FileSystemFontStorageService(monitor.Object, NullLogger<FileSystemFontStorageService>.Instance);
    }

    [Fact]
    public async Task AFont_IsStoredByteForByte_UnderTheHashOfItsContent()
    {
        var file = Woff2("Stored Sans");

        var stored = await Store().SaveAsync(new MemoryStream(file), CancellationToken.None);

        stored.IsError.Should().BeFalse();
        stored.Value.Key.Should().MatchRegex("^fonts/[0-9a-f]{64}\\.woff2$");
        stored.Value.FamilyName.Should().Be("Stored Sans");
        File.ReadAllBytes(Path.Combine(_root, stored.Value.Key)).Should().Equal(file);
    }

    [Fact]
    public async Task TheSameFontTwice_IsOneFile()
    {
        var file = Woff2();

        var first = await Store().SaveAsync(new MemoryStream(file), CancellationToken.None);
        var second = await Store().SaveAsync(new MemoryStream(file), CancellationToken.None);

        second.Value.Key.Should().Be(first.Value.Key);
        Directory.GetFiles(Path.Combine(_root, "fonts")).Should().ContainSingle();
    }

    [Fact]
    public async Task AFileThatIsNotAFont_IsRefused_AndNothingIsWritten()
    {
        var stored = await Store().SaveAsync(new MemoryStream("<html></html>"u8.ToArray()), CancellationToken.None);

        stored.FirstError.Code.Should().Be("Font.Invalid");
        Directory.Exists(Path.Combine(_root, "fonts")).Should().BeFalse();
    }

    [Fact]
    public async Task ABodyLongerThanTheLimit_IsRefused_WithoutReadingAllOfIt()
    {
        var body = new byte[PlatformFonts.MaxSizeBytes + 1];

        var stored = await Store().SaveAsync(new MemoryStream(body), CancellationToken.None);

        stored.FirstError.Code.Should().Be("Font.FileTooLarge");
    }

    [Theory]
    [InlineData("fonts/../../outside.woff2")]
    [InlineData("../outside.woff2")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("fonts/ABC.woff2")]
    public async Task OnlyAKeyTheStoreMinted_IsEverDeleted(string key)
    {
        Directory.CreateDirectory(_root);
        var outside = Path.Combine(_root, "outside.woff2");
        File.WriteAllText(outside, "keep");

        await Store().DeleteAsync(key, CancellationToken.None);

        File.Exists(outside).Should().BeTrue();
    }

    [Fact]
    public async Task TheSweep_DeletesOnlyOldFilesNoRoleUses()
    {
        var store = Store();
        var kept = (await store.SaveAsync(new MemoryStream(Woff2("Kept")), CancellationToken.None)).Value.Key;
        var old = (await store.SaveAsync(new MemoryStream(Woff2("Old")), CancellationToken.None)).Value.Key;
        var fresh = (await store.SaveAsync(new MemoryStream(Woff2("Fresh")), CancellationToken.None)).Value.Key;
        File.SetLastWriteTimeUtc(Path.Combine(_root, kept), DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(Path.Combine(_root, old), DateTime.UtcNow.AddDays(-2));

        await store.PruneAsync([kept], DateTime.UtcNow.AddHours(-1), CancellationToken.None);

        File.Exists(Path.Combine(_root, kept)).Should().BeTrue("a role uses it");
        File.Exists(Path.Combine(_root, old)).Should().BeFalse("no role uses it and it is old");
        File.Exists(Path.Combine(_root, fresh)).Should().BeTrue("a save running beside this one may be about to record it");
    }

    // --- serving ---

    [Fact]
    public async Task AFont_IsServedAsWoff2_CrossOrigin_Immutable_NoSniff()
    {
        var stored = (await Store().SaveAsync(new MemoryStream(Woff2()), CancellationToken.None)).Value;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseStaticFiles(UploadsStaticFiles.Options(_root, "/uploads/images"));
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync($"/uploads/images/{stored.Key}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("font/woff2");
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal("*");
        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.CacheControl!.ToString().Should().Contain("immutable").And.Contain("max-age=31536000");

        // Nothing outside the volume is reachable through the path.
        (await app.GetTestClient().GetAsync("/uploads/images/fonts/..%2F..%2Fappsettings.json"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AnImage_KeepsItsOwnHeaders_AndNoCors()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(Path.Combine(_root, "logo.webp"), [1, 2, 3]);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseStaticFiles(UploadsStaticFiles.Options(_root, "/uploads/images"));
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/uploads/images/logo.webp");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        response.Headers.CacheControl!.ToString().Should().Be("public, max-age=86400");
    }

    // --- the value and the rule ---

    [Theory]
    [InlineData(0, "Font.FileRequired")]
    [InlineData(PlatformFonts.MaxSizeBytes + 1, "Font.FileTooLarge")]
    public void TheSizeRule_IsTheOwners(long length, string code)
    {
        PlatformFonts.MaxSizeBytes.Should().Be(2 * 1024 * 1024, "D7: at most 2 MB each");
        PlatformFont.CheckFileSize(length).FirstError.Code.Should().Be(code);
        PlatformFont.CheckFileSize(PlatformFonts.MaxSizeBytes).IsError.Should().BeFalse();
    }

    [Fact]
    public void AFamilyName_IsCleanedOfControlCharacters_AndCut()
    {
        PlatformFont.Create("fonts/x.woff2", "  Evil\u0000\u001b[31m Sans\n ").FamilyName.Should().Be("Evil[31m Sans");
        PlatformFont.Create("fonts/x.woff2", new string('A', 300)).FamilyName.Should().HaveLength(PlatformFonts.MaxFamilyNameLength);
        PlatformFont.Create("fonts/x.woff2", " \u0007 ").FamilyName.Should().BeNull();
    }

    // --- the command ---

    private sealed class Harness
    {
        public Mock<IPlatformSettingsRepository> Repository { get; } = new();
        public Mock<IFontStorageService> Storage { get; } = new();
        public List<PlatformFontChangedEvent> Published { get; } = [];
        public List<string> Order { get; } = [];
        public PlatformSettings Settings { get; }

        public Harness(PlatformFont? heading = null, PlatformFont? body = null, string newKey = "fonts/new.woff2")
        {
            var fonts = new Dictionary<FontRole, PlatformFont>();
            if (heading is not null)
            {
                fonts[FontRole.Heading] = heading;
            }

            if (body is not null)
            {
                fonts[FontRole.Body] = body;
            }

            Settings = new PlatformSettings(
                PlatformSettings.SingletonId, "Acme", null, null, null, null, null, fonts: fonts);
            Repository.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Settings);
            Repository.Setup(r => r.UpdateFontsAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()))
                .Callback(() => Order.Add("save"))
                .Returns(Task.CompletedTask);
            Storage.Setup(s => s.SaveAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Callback(() => Order.Add("store"))
                .ReturnsAsync(new StoredFont(newKey, "New Sans"));
            Storage.Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Callback((string key, CancellationToken _) => Order.Add($"delete:{key}"))
                .Returns(Task.CompletedTask);
            Storage.Setup(s => s.PruneAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Callback((IReadOnlyCollection<string> keep, DateTime _, CancellationToken _) =>
                    Order.Add($"prune:{string.Join(",", keep.Order())}"))
                .Returns(Task.CompletedTask);
        }

        public Task<ErrorOr<PlatformSettingsDto>> Send(FontRole role, long? length = 1000, string? fileName = "upload.woff2")
        {
            var publisher = new Mock<IPublisher>();
            publisher.Setup(p => p.Publish(It.IsAny<PlatformFontChangedEvent>(), It.IsAny<CancellationToken>()))
                .Callback((PlatformFontChangedEvent e, CancellationToken _) =>
                {
                    Published.Add(e);
                    Order.Add("publish");
                })
                .Returns(Task.CompletedTask);
            var composer = new Mock<IImageUrlComposer>();
            composer.Setup(c => c.Compose(It.IsAny<string?>()))
                .Returns<string?>(key => key is null ? null : $"https://api.test/uploads/images/{key}");
            var upload = length is null ? null : new FontUpload(new MemoryStream(), length.Value, fileName);
            return new SetPlatformFontCommandHandler(
                    Repository.Object,
                    new Mock<IUserRepository>().Object,
                    Storage.Object,
                    composer.Object,
                    publisher.Object,
                    TimeProvider.System,
                    NullLogger<SetPlatformFontCommandHandler>.Instance)
                .Handle(new SetPlatformFontCommand(role, upload, Admin), CancellationToken.None);
        }
    }

    [Fact]
    public async Task AFirstFont_IsStored_Recorded_Announced_AndAnswered()
    {
        var harness = new Harness();

        var result = await harness.Send(FontRole.Heading);

        result.IsError.Should().BeFalse();
        result.Value.Fonts.Heading!.Url.Should().Be("https://api.test/uploads/images/fonts/new.woff2");
        result.Value.Fonts.Heading.FamilyName.Should().Be("New Sans");
        result.Value.Fonts.Body.Should().BeNull();
        harness.Order.Should().Equal("store", "save", "publish", "prune:fonts/new.woff2");
        var published = harness.Published.Should().ContainSingle().Subject;
        published.Role.Should().Be(FontRole.Heading);
        published.BeforeKey.Should().BeNull();
        published.AfterKey.Should().Be("fonts/new.woff2");
        published.TriggeredBy.Should().Be(Admin);
        harness.Repository.Verify(r => r.UpdateAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Repository.Verify(r => r.UpdateThemeAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AReplacedFont_HasItsFileDeleted()
    {
        var harness = new Harness(body: PlatformFont.Create("fonts/old.woff2", "Old"));

        await harness.Send(FontRole.Body);

        harness.Order.Should().Equal("store", "save", "publish", "delete:fonts/old.woff2", "prune:fonts/new.woff2");
        harness.Published.Single().BeforeFamilyName.Should().Be("Old");
    }

    [Fact]
    public async Task AReplacedFile_ThatTheOtherRoleUses_IsKept()
    {
        var shared = PlatformFont.Create("fonts/shared.woff2", "Shared");
        var harness = new Harness(heading: shared, body: shared);

        await harness.Send(FontRole.Body);

        harness.Order.Should().NotContain(step => step.StartsWith("delete:"));
        harness.Order.Last().Should().Be("prune:fonts/new.woff2,fonts/shared.woff2");
    }

    [Fact]
    public async Task TheFontARoleAlreadyHas_WritesAndAnnouncesNothing()
    {
        var harness = new Harness(heading: PlatformFont.Create("fonts/new.woff2", "New Sans"));

        var result = await harness.Send(FontRole.Heading);

        result.IsError.Should().BeFalse();
        harness.Order.Should().Equal("store", "prune:fonts/new.woff2");
    }

    [Fact]
    public async Task RemovingAFont_RecordsNone_AndDeletesItsFile()
    {
        var harness = new Harness(heading: PlatformFont.Create("fonts/old.woff2", "Old"));

        var result = await harness.Send(FontRole.Heading, length: null);

        result.Value.Fonts.Heading.Should().BeNull();
        harness.Settings.FontFor(FontRole.Heading).Should().BeNull();
        harness.Order.Should().Equal("save", "publish", "delete:fonts/old.woff2", "prune:");
        harness.Published.Single().AfterKey.Should().BeNull();
    }

    [Fact]
    public async Task RemovingAFontThatIsNotSet_ChangesNothing()
    {
        var harness = new Harness();

        await harness.Send(FontRole.Body, length: null);

        harness.Order.Should().Equal("prune:");
    }

    [Theory]
    [InlineData(0L, "Font.FileRequired")]
    [InlineData(PlatformFonts.MaxSizeBytes + 1, "Font.FileTooLarge")]
    public async Task AFileOfTheWrongSize_IsRefused_BeforeItIsRead(long length, string code)
    {
        var harness = new Harness();

        var result = await harness.Send(FontRole.Body, length);

        result.FirstError.Code.Should().Be(code);
        harness.Order.Should().BeEmpty();
    }

    [Fact]
    public async Task AFileTheStoreRefuses_ChangesNothing()
    {
        var harness = new Harness();
        harness.Storage.Setup(s => s.SaveAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FontErrors.Invalid);

        var result = await harness.Send(FontRole.Body);

        result.FirstError.Code.Should().Be("Font.Invalid");
        harness.Repository.Verify(r => r.UpdateFontsAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()), Times.Never);
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedSave_TakesBackTheFileItWrote()
    {
        var harness = new Harness(body: PlatformFont.Create("fonts/old.woff2", "Old"));
        harness.Repository.Setup(r => r.UpdateFontsAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var act = () => harness.Send(FontRole.Body);

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Order.Should().Equal("store", "delete:fonts/new.woff2");
        harness.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedSave_KeepsAFileAnotherRoleAlreadyUses()
    {
        var harness = new Harness(heading: PlatformFont.Create("fonts/new.woff2", "New Sans"));
        harness.Repository.Setup(r => r.UpdateFontsAsync(It.IsAny<PlatformSettings>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("database down"));

        var act = () => harness.Send(FontRole.Body);

        await act.Should().ThrowAsync<InvalidOperationException>();
        harness.Order.Should().Equal("store");
    }

    [Fact]
    public async Task AFontThatDeclaresNoFamily_IsNamedAfterItsFile()
    {
        var harness = new Harness();
        harness.Storage.Setup(s => s.SaveAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StoredFont("fonts/new.woff2", null));

        var result = await harness.Send(FontRole.Body, fileName: "Brand-Regular.woff2");

        result.Value.Fonts.Body!.FamilyName.Should().Be("Brand-Regular");
    }

    // --- the audit row ---

    [Fact]
    public async Task AChange_IsAudited_UnderThePlatformSettingsAction()
    {
        var audit = new Mock<IAuditLogRepository>();
        AuditLogEntity? written = null;
        audit.Setup(a => a.CreateAsync(It.IsAny<AuditLogEntity>(), It.IsAny<CancellationToken>()))
            .Callback((AuditLogEntity log, CancellationToken _) => written = log)
            .Returns(Task.CompletedTask);

        await new PlatformFontChangedAuditEventHandler(audit.Object, NullLogger<PlatformFontChangedAuditEventHandler>.Instance)
            .Handle(
                new PlatformFontChangedEvent(Guid.NewGuid(), PlatformSettings.SingletonId, FontRole.Heading,
                    "Old", "New", "fonts/old.woff2", "fonts/new.woff2", Admin, DateTimeOffset.UtcNow),
                CancellationToken.None);

        written!.Action.Should().Be(AuditActions.PlatformSettingsUpdated);
        written.PerformedBy.Should().Be(Admin);
        written.OldValues.Should().Contain("\"font\":\"heading\"").And.Contain("\"familyName\":\"Old\"");
        written.NewValues.Should().Contain("\"file\":\"fonts/new.woff2\"");
    }

    // --- the schema ---

    private static string TableSource() =>
        File.ReadAllText(Directory.GetFiles(
                Path.Combine(ApiSourceScan.SolutionDirectory(), "Auth_DB", "dbo", "Tables"),
                "PlatformSettings.sql",
                SearchOption.AllDirectories)
            .Should().ContainSingle().Subject);

    [Fact]
    public void TheFontColumns_AreAppendedLast_NullableWithoutADefault()
    {
        var columns = OrganizationCreationSchemaTests.DeclaredColumns(TableSource(), "PlatformSettings");

        columns.Select(c => c.Name).TakeLast(4).Should().Equal(
            ["HeadingFontKey", "HeadingFontName", "BodyFontKey", "BodyFontName"],
            "appended, the publish is an ALTER TABLE ADD, not a rebuild");
        columns.TakeLast(4).Should().OnlyContain(c => Regex.IsMatch(c.Definition, @"^NVARCHAR\(100\)\s+NULL$"));
    }

    [Theory]
    [InlineData("Heading")]
    [InlineData("Body")]
    public void AFontsName_NeedsItsFile_UnderANamedConstraint(string role)
    {
        TableSource().Should().Contain(
            $"CONSTRAINT [CK_PlatformSettings_{role}Font] CHECK ([{role}FontName] IS NULL OR [{role}FontKey] IS NOT NULL)");
    }

    [Fact]
    public void OnlyTheDedicatedStatement_WritesTheFonts()
    {
        var source = File.ReadAllText(Path.Combine(
            ApiSourceScan.SolutionDirectory(), "Auth.Infrastructure", "Persistence", "PlatformSettingsRepository.cs"));

        Regex.Matches(source, @"\[BodyFontKey\] = @BodyFontKey").Should().ContainSingle(
            "a full save built from a stale copy must never put back an older font");
    }
}
