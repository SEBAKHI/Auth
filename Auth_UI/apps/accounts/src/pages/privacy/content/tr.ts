import { CONTROLLER } from "./details"
import type { PrivacyPolicyContent } from "./types"

export const tr: PrivacyPolicyContent = {
  title: "Gizlilik Politikası ve Aydınlatma Metni",
  effectiveDate: "Yürürlük tarihi: 28 Temmuz 2026",
  versionLabel: "Sürüm",
  unfilledWarning:
    "Taslak — veri sorumlusu bilgileri henüz doldurulmamıştır. Tüm alanlar tamamlanmadan bu sayfa yayımlanmamalıdır.",
  contactDpoLabel: "Veri koruma görevlisi",
  contactVerbisLabel: "VERBİS kayıt numarası",
  contactKepLabel: "Kayıtlı elektronik posta (KEP)",
  intro: [
    `Bu metin, ${CONTROLLER.legalName} hesap hizmetinin hangi kişisel verileri topladığını, bunları neden işlediğini, ne kadar süreyle sakladığını ve verileriniz üzerindeki haklarınızı — hesabınızı ve hesabınıza bağlı her şeyi nasıl sileceğiniz dahil — açıklar.`,
    "Bu metin, birincil uyum çerçevemiz olan 6698 sayılı Kişisel Verilerin Korunması Kanunu'nun (KVKK) 10. maddesi uyarınca veri sorumlusunun aydınlatma yükümlülüğünü yerine getirmek üzere hazırlanmış olup ayrıca AB/AEA Genel Veri Koruma Tüzüğü (GDPR) ve değişik haliyle Kaliforniya Tüketici Gizliliği Yasası (CCPA/CPRA) gerekliliklerini karşılayacak şekilde kaleme alınmıştır. Nerede yaşarsanız yaşayın size aynı kontrolleri sunarız.",
  ],
  sections: [
    {
      heading: "Topladığımız veriler",
      paragraphs: [
        "Yalnızca hesap hizmetinin çalışması için gereken verileri toplarız. Bundan fazlasını asla istemeyiz; isteğe bağlı alanlar açıkça isteğe bağlıdır.",
      ],
      bullets: [
        "Hesap ve profil: e-posta adresi, ad ve soyad, isteğe bağlı görünen ad, isteğe bağlı telefon numarası (şifrelenmiş olarak saklanır), isteğe bağlı profil fotoğrafı, tercih edilen dil, saat dilimi ve tema.",
        "Kimlik bilgileri ve güvenlik ayarları: parolanız (yalnızca tek yönlü Argon2id özeti olarak saklanır — parolanızı okuyamayız), isteğe bağlı iki adımlı doğrulama anahtarı ve kurtarma kodları (şifrelenmiş olarak saklanır) ile parola değişiklik geçmişi (yeniden kullanımı önlemek için, yalnızca özet olarak).",
        "Google veya Apple ile oturum açma: sağlayıcının paylaştığı kimlik tanımlayıcınız, e-posta adresiniz ve adınız. Apple için, yalnızca hesabınızı sildiğinizde Apple'ın oturum açma iznini iptal edebilmemiz amacıyla şifrelenmiş bir iptal belirteci saklanır.",
        "Güvenlik ve kullanım kayıtları: oturum açma denemeleri (zaman, IP adresi, tarayıcı tanımlayıcısı, sonuç), etkin oturumlar ve belirteçler ile hesapla ilgili işlemlerin denetim günlüğü.",
        "İletişim: size gönderdiğimiz hizmet e-postalarının kaydı (doğrulama kodları, güvenlik bildirimleri, silme onayları).",
      ],
    },
    {
      heading: "İşleme amaçları ve hukuki sebepler",
      paragraphs: [
        "Verileriniz, hizmetin form ve oturum açma akışları üzerinden elektronik ortamda toplanır. Her amaç, KVKK'nın 5. maddesindeki bir hukuki sebebe ve GDPR'ın 6. maddesindeki karşılığına dayanır:",
      ],
      bullets: [
        "Hesabınızı sunmak — kimlik doğrulama, oturumlar, profil, organizasyon üyeliği (sözleşmenin ifası için gerekli olması: KVKK m. 5/2-c; GDPR m. 6/1-b).",
        "Hesapları güvende tutmak — oturum açma denemesi kayıtları, hız sınırlama, oturum iptali, denetim günlüğü, dolandırıcılığın önlenmesi (meşru menfaat: KVKK m. 5/2-f; GDPR m. 6/1-f).",
        "Hukuki yükümlülükleri yerine getirmek — bir silme talebinin yerine getirildiğine dair asgari kanıtın saklanması (KVKK m. 5/2-ç; GDPR m. 6/1-c).",
        "İsteğe bağlı veriler — telefon numarası ve profil fotoğrafı yalnızca siz sağlamayı seçtiğiniz için işlenir ve bunları dilediğiniz an kaldırabilirsiniz (açık rıza: KVKK m. 5/1; GDPR m. 6/1-a).",
      ],
    },
    {
      heading: "Yapmadıklarımız",
      paragraphs: [
        "Kişisel verilerinizi satmayız ve bağlamlar arası davranışsal reklamcılık için paylaşmayız — CCPA terimleriyle, son 12 ayda hiçbir \"satış\" veya \"paylaşım\" gerçekleşmemiştir ve planlanmamaktadır. Hesap hizmetinde reklam veya üçüncü taraf analitik izleyicileri çalıştırmayız; verilerinizi, hukuki ya da benzer ölçüde önemli sonuçlar doğuran otomatik kararlar için kullanmayız.",
      ],
    },
    {
      heading: "Çerezler ve yerel depolama",
      paragraphs: [
        "Hesap hizmeti yalnızca oturum açmanın kesin olarak gerektirdiğini kullanır: kendi sayfalarımız arasında oturumunuzu koruyan zorunlu bir oturum çerezi ile oturum yenileme belirtecinizi ve dil/tema tercihlerinizi tutan tarayıcı yerel depolaması. Analitik, reklam veya siteler arası izleme çerezi yoktur.",
      ],
    },
    {
      heading: "Verileri kimlerle paylaşırız",
      paragraphs: [
        "Kişisel verileri yalnızca hizmeti işleten veri işleyenlerle ve yalnızca gerektiği ölçüde paylaşırız:",
      ],
      bullets: [
        "Google (Google ile oturum açma) ve Apple (Apple ile oturum açma) — yalnızca onlarla oturum açmayı seçtiğinizde; bu alışveriş, ilgili sağlayıcının kendi gizlilik politikasına tabidir.",
        `E-posta gönderim sağlayıcımız ${CONTROLLER.emailProvider} — yukarıda açıklanan hizmet e-postalarını göndermek için.`,
        `Barındırma sağlayıcımız ${CONTROLLER.hostingProvider} — hizmetin verilerini saklar.`,
        "Kamu makamları — yalnızca geçerli bir hukuki talep bizi buna zorlarsa.",
      ],
    },
    {
      heading: "Yurt dışına aktarım",
      paragraphs: [
        `Hizmet ${CONTROLLER.hostingCountry} ülkesinde barındırılmaktadır. Kişisel verilerin Türkiye dışına aktarılması hâlinde bu aktarım KVKK'nın 9. maddesi kapsamında yapılır: Kişisel Verileri Koruma Kurulunca yeterlilik kararı bulunan hâllerde bu karara, bulunmayan hâllerde ise maddenin öngördüğü uygun güvencelere (Kuruma bildirilmesi kaydıyla Kurulca ilan edilen standart sözleşme gibi) dayanırız. AEA veya Birleşik Krallık'tan çıkan veriler için ayrıca yeterlilik kararlarına veya Avrupa Komisyonu'nun Standart Sözleşme Maddelerine dayanırız. Aşağıdaki güvenlik önlemleri her durumda uygulanır.`,
      ],
    },
    {
      heading: "Verileri nasıl koruruz",
      paragraphs: ["Güvenlik katmanlıdır ve her hesap için geçerlidir:"],
      bullets: [
        "Parolalar Argon2id ile özetlenir; doğrulama kodları yalnızca özet olarak, sıfırlama bağlantıları yalnızca HMAC özeti olarak saklanır.",
        "Telefon numaranız, iki adımlı doğrulama anahtarınız ve Apple iptal belirteciniz, hesabınıza özgü bir anahtar altında AES-256-GCM ile şifrelenir.",
        "Tüm trafik aktarım sırasında şifrelenir (TLS). Oturum açma, hız sınırlama ve hesap kilitleme ile korunur; oturumlar her an iptal edilebilir ve silme talebinde bulunduğunuzda tamamı derhâl iptal edilir.",
        "Hesap işlemleri, şüpheli etkinliğin tespit edilip incelenebilmesi için denetim günlüğüne kaydedilir.",
      ],
    },
  ],
  retention: {
    heading: "Verileri ne kadar saklarız",
    intro:
      "Kişisel verileri amacın gerektirdiğinden daha uzun süre saklamayız. Bu süreler elle inceleme ile değil, sistem tarafından otomatik olarak uygulanır:",
    columns: ["Veri", "Saklama süresi", "Süre sonunda"],
    rows: [
      {
        category: "Hesap ve profil verileri",
        retention: "Hesabınızı silene kadar (+ 30 günlük kurtarma süresi)",
        detail:
          "Aşağıda açıklanan aşamalı silme süreciyle kalıcı olarak yok edilir.",
      },
      {
        category: "Parola özetleri ve iki adımlı doğrulama anahtarları",
        retention: "Hesap silinene kadar",
        detail:
          "Yalnızca özet veya şifrelenmiş olarak saklanır; hesapla birlikte yok edilir.",
      },
      {
        category: "Oturumlar ve belirteçler",
        retention: "Süresi dolana veya oturum kapatılana kadar",
        detail: "Silme talep edildiğinde tamamı derhâl iptal edilir.",
      },
      {
        category: "Oturum açma denemesi kayıtları (IP adresi dahil)",
        retention: "365 gün",
        detail:
          "Otomatik olarak silinir; hesap silindiğinde derhâl kimliksizleştirilir.",
      },
      {
        category: "Güvenlik denetim günlüğü",
        retention: "Hesap silindiğinde kimliksizleştirilir",
        detail:
          "Tüm kişisel alanlar kaldırılır; silme işleminin gerçekleştirildiği bilgisi (yalnızca olay türü ve zaman damgası) hukuki kanıt olarak en az 3 yıl saklanır.",
      },
      {
        category: "Gönderilen hizmet e-postalarının kaydı",
        retention: "180 gün",
        detail: "Otomatik olarak silinir.",
      },
      {
        category: "Silme doğrulama kodları",
        retention: "15 dakika",
        detail:
          "Yalnızca özet olarak saklanır; süresi dolan kodlar silinir.",
      },
      {
        category: "Silme kaydı (özetlenmiş tanımlayıcılar)",
        retention: "Kalıcı",
        detail:
          "Silinen e-posta ve kullanıcı adının tek yönlü HMAC özetleri; silinen tanımlayıcıların bir başkası tarafından asla yeniden kaydedilememesi için tutulur. Okunabilir hiçbir kişisel veri içermez.",
      },
      {
        category: "Yedekler",
        retention: "En fazla 6 ay",
        detail:
          "Hesabınız silindiğinde, şifrelenmiş alanlarınızın şifreleme anahtarı yok edilir; bu sayede söz konusu veriler mevcut yedeklerin içinde bile kalıcı olarak okunamaz hâle gelir.",
      },
    ],
  },
  deletion: {
    heading: "Hesabınızı silme",
    paragraphs: [
      "Hesabınızın ve kişisel verilerinizin kalıcı olarak silinmesini, kimseyle iletişime geçmeden, dilediğiniz an talep edebilirsiniz. Talep derhâl kayda alınır ve en geç 30 gün içinde tamamlanır:",
    ],
    bullets: [
      "Hesabınız derhâl devre dışı bırakılır ve tüm cihazlarda oturumlar kapatılır; tüm oturumlar, belirteçler ve oturum açma izinleri anında iptal edilir.",
      "30 gün boyunca fikrinizi değiştirebilirsiniz — yeniden oturum açmak hesabı geri yükler ve silmeyi iptal eder. Her adımda onay e-postası alırsınız.",
      "30 günlük sürenin ardından silme otomatik olarak gerçekleştirilir ve geri alınamaz: profil verileri silinir, güvenlik kayıtları kimliksizleştirilir, hesaba özgü şifreleme anahtarları yok edilir (yedekleri de kapsayan kriptografik imha) ve — Apple ile oturum açtıysanız — Apple'a oturum açma iznini iptal etmesi bildirilir.",
      "E-posta adresiniz ve kullanıcı adınız asla yeniden kullanıma açılmaz: geriye yalnızca tek yönlü özetler kalır; böylece bunları bir başkası asla kaydedemez.",
    ],
    button: "Hesabımı sil",
    signedInHint:
      "Oturum açtınız mı? Bunu profilinizin Hesap sekmesinden (Tehlikeli bölge) de yapabilirsiniz.",
  },
  rights: [
    {
      heading: "Türkiye'deki haklarınız (KVKK)",
      paragraphs: [
        "6698 sayılı Kanun'un 11. maddesi uyarınca ilgili kişi olarak; kişisel verilerinizin işlenip işlenmediğini öğrenme, işlenmişse buna ilişkin bilgi talep etme, işlenme amacını ve amacına uygun kullanılıp kullanılmadığını öğrenme, yurt içinde veya yurt dışında aktarıldığı üçüncü kişileri bilme, eksik veya yanlış işlenmişse düzeltilmesini isteme, 7. madde çerçevesinde silinmesini veya yok edilmesini isteme, düzeltme ve silme işlemlerinin aktarıldığı üçüncü kişilere bildirilmesini isteme, münhasıran otomatik sistemlerle analiz edilmesi suretiyle aleyhinize bir sonucun ortaya çıkmasına itiraz etme ve kanuna aykırı işleme sebebiyle zarara uğramanız hâlinde zararın giderilmesini talep etme haklarına sahipsiniz.",
        "Başvurular, Veri Sorumlusuna Başvuru Usul ve Esasları Hakkında Tebliğ uyarınca aşağıdaki iletişim bilgileri üzerinden — yazılı olarak, varsa kayıtlı elektronik posta (KEP) adresimiz aracılığıyla veya bize daha önce bildirdiğiniz e-posta adresinizden — yapılabilir. Başvurular en geç 30 gün içinde ve ücretsiz olarak sonuçlandırılır. Başvurunuzun reddedilmesi veya yanıtsız kalması hâlinde Kişisel Verileri Koruma Kurulu'na şikâyette bulunabilirsiniz.",
      ],
    },
    {
      heading: "AEA ve Birleşik Krallık'taki haklarınız (GDPR)",
      paragraphs: [
        "Verilerinize erişme, düzeltilmesini isteme, silinmesini isteme, işlemeyi kısıtlama veya işlemeye itiraz etme, taşınabilir bir kopyasını alma ve rızaya dayalı işlemede rızanızı dilediğiniz an geri çekme hakkınız vardır. Bunların çoğunu doğrudan uygulama içinde kullanabilirsiniz (profil düzenleme, hesap silme); geri kalanı için aşağıdaki bilgilerden bize ulaşın. Ayrıca ulusal denetim makamınıza şikâyette bulunma hakkınız vardır.",
      ],
    },
    {
      heading: "Kaliforniya'daki haklarınız (CCPA/CPRA)",
      paragraphs: [
        "Hangi kişisel bilgileri topladığımızı öğrenme ve bunlara erişme, düzeltme, silme ve bu hakları kullandığınız için ayrımcılığa uğramama hakkınız vardır. Kişisel bilgileri satmadığımız veya paylaşmadığımız ve hassas kişisel bilgileri yalnızca hizmeti sunmak için kullandığımız için vazgeçilecek bir işlem yoktur. Doğrulanmış talepleri 45 gün içinde yerine getiririz. Talebinizi sizin adınıza yetkili bir temsilci aracılığıyla da iletebilirsiniz.",
      ],
    },
    {
      heading: "Diğer tüm kullanıcılar",
      paragraphs: [
        "Aynı kontroller — erişim, düzeltme, silme ve yukarıdaki uygulama içi silme akışı — nerede yaşarsanız yaşayın her kullanıcıya sunulur.",
      ],
    },
  ],
  closing: [
    {
      heading: "Çocuklar",
      paragraphs: [
        "Hizmet çocuklara yönelik değildir ve 16 yaşından küçükler tarafından kullanılamaz. Çocuklara ait verileri bilerek toplamayız; bir çocuğun hesap oluşturduğunu düşünüyorsanız bize bildirin, hesabı sileriz.",
      ],
    },
    {
      heading: "Bu politikadaki değişiklikler",
      paragraphs: [
        "Bu politikanın her revizyonu bir sürüm numarası taşır (yıl.ay, sayfanın başında gösterilir). Önemli değişiklikler yapmamız hâlinde, yürürlüğe girmeden önce sizi e-posta veya uygulama içi bildirimle bilgilendiririz. Silme talepleri her zaman, talebin yapıldığı tarihte yürürlükte olan koşullara göre yerine getirilir.",
      ],
    },
    {
      heading: "İletişim ve şikâyet",
      paragraphs: [
        `Veri sorumlusu: ${CONTROLLER.legalName}, ${CONTROLLER.address}. Gizlilik iletişimi: ${CONTROLLER.privacyEmail}. Hak taleplerini KVKK/GDPR kapsamında 30 gün, CCPA kapsamında 45 gün içinde yanıtlarız. Sonuçtan memnun kalmazsanız denetim makamına şikâyette bulunabilirsiniz: Türkiye'de Kişisel Verileri Koruma Kurumu, AEA/Birleşik Krallık'ta ulusal veri koruma makamınız, Kaliforniya'da California Privacy Protection Agency veya Başsavcılık.`,
      ],
    },
  ],
}
