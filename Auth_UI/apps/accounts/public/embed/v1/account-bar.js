/*
 * The IAM account bar for linked applications (owner decisions of 2026-11-02).
 *
 * A linked application on a subdomain of the IAM site adds this script once and puts
 * <iam-account-bar> in its header. The element draws the grid button and the avatar in a
 * frame served by the accounts app (embed/v1/bar.html) and opens their panels as frames of
 * their own (embed/v1/panel.html), positioned under it. The frames read the person from
 * IAM themselves; this script never sees who is signed in.
 *
 * Attributes:
 *   hide="apps" | "avatar"  leaves one of the two out (an application with its own
 *                           account menu hides the avatar).
 *   signout-href="..."      the application's own sign-out address. It ends the
 *                           application's session and then IAM's. Without it the menu
 *                           offers no Sign out: ending IAM's session alone would leave the
 *                           application's alive.
 *   signin-href="..."       where Sign in takes the page; the accounts sign-in page if absent.
 *   theme="light" | "dark"  the application's colour mode, so the bar matches its header;
 *                           the person's own choice in IAM if absent.
 *
 * Before it takes the page to sign out or to sign in, the element fires a cancelable
 * "iam-account-bar:navigate" event, detail { action: "signout" | "signin", href }. A
 * single-page application that cancels it navigates itself (through its router, after
 * clearing its own state); otherwise the script sets the address.
 *
 * What crosses between this page and the frames carries no data about the person: requests
 * to open, close, resize, sign out and sign in. Messages are accepted only from the accounts
 * origin (this script's own) and only from the frames this element made. Sign out and sign
 * in navigate this page, from here; the frames are sandboxed without top navigation.
 *
 * Its address is stable (/embed/v1/): a change that breaks the attributes or the messages is
 * a /v2/. apps/accounts/src/embed/protocol.ts is the frames' side of the same messages, and
 * account-bar-loader.test.ts holds the two together.
 */
(function () {
  "use strict";

  var ELEMENT = "iam-account-bar";
  var NS = "iam-account-bar";
  if (!window.customElements || window.customElements.get(ELEMENT)) return;

  var script =
    document.currentScript ||
    document.querySelector('script[src*="/embed/v1/account-bar.js"]');
  if (!script || !script.src) return;
  var ACCOUNTS = new URL(script.src, location.href).origin;
  var BASE = ACCOUNTS + "/embed/v1/";

  /** The gap between the bar and a panel, and the margin a panel keeps from the screen. */
  var GAP = 4;
  var EDGE = 8;

  var PANELS = ["apps", "account"];
  var HIDE = ["apps", "avatar"];
  var THEMES = ["light", "dark"];

  function oneOf(values, value) {
    return values.indexOf(value) >= 0 ? value : null;
  }

  /** An address to send this page to: http(s) only, http only on the loopback host. */
  function safeHref(raw) {
    if (!raw) return null;
    var url;
    try {
      url = new URL(raw, location.href);
    } catch (error) {
      return null;
    }
    if (url.protocol === "https:") return url.href;
    var loopback = url.hostname === "localhost" || url.hostname === "127.0.0.1" || url.hostname === "[::1]";
    return url.protocol === "http:" && loopback ? url.href : null;
  }

  function navigateTop(element, action, href) {
    var event = new CustomEvent("iam-account-bar:navigate", {
      bubbles: true,
      cancelable: true,
      detail: { action: action, href: href },
    });
    if (!element.dispatchEvent(event)) return;
    try {
      window.top.location.href = href;
    } catch (error) {
      window.location.href = href;
    }
  }

  var FRAME_CSS =
    ":host{display:inline-flex;vertical-align:middle}" +
    "iframe{display:block;border:0;margin:0;padding:0;background:transparent;color-scheme:normal}" +
    // Never display:none: a frame with no box lays nothing out, so the bar inside would
    // measure 0 x 0 and never ask to be shown. Empty, it is one invisible pixel out of flow.
    "iframe[data-empty]{position:absolute;visibility:hidden;width:1px!important;height:1px!important}";
  var LAYER_CSS =
    "iframe{position:fixed;border:0;margin:0;padding:0;background:transparent;color-scheme:normal;" +
    "z-index:2147483000;visibility:hidden;pointer-events:none}" +
    "iframe[data-shown]{visibility:visible;pointer-events:auto}";

  /**
   * Styles for a shadow root, as a constructed stylesheet: CSSOM, which the application's
   * own CSP does not govern, where a <style> element would need its style-src to allow
   * inline styles. A <style> only where the browser has no constructed stylesheets.
   */
  function adopt(root, css) {
    if (typeof CSSStyleSheet === "function" && "adoptedStyleSheets" in root) {
      try {
        var sheet = new CSSStyleSheet();
        sheet.replaceSync(css);
        root.adoptedStyleSheets = [sheet];
        return;
      } catch (error) {
        /* fall through */
      }
    }
    var style = document.createElement("style");
    style.textContent = css;
    root.appendChild(style);
  }

  function frame(src, title, sandbox) {
    var element = document.createElement("iframe");
    element.src = src;
    element.title = title;
    element.setAttribute("sandbox", sandbox);
    element.setAttribute("referrerpolicy", "strict-origin");
    element.setAttribute("scrolling", "no");
    return element;
  }

  var instances = [];

  class AccountBar extends HTMLElement {
    static get observedAttributes() {
      return ["hide", "signout-href", "signin-href", "theme"];
    }

    constructor() {
      super();
      this._shadow = this.attachShadow({ mode: "closed" });
      this._bar = null;
      this._barInset = 0;
      this._layer = null;
      this._panels = {};
      this._open = null;
      this._frame = 0;
      this._onPointerDown = this._onPointerDown.bind(this);
      this._onKeyDown = this._onKeyDown.bind(this);
      this._onViewport = this._onViewport.bind(this);
    }

    connectedCallback() {
      instances.push(this);
      document.addEventListener("pointerdown", this._onPointerDown, true);
      document.addEventListener("keydown", this._onKeyDown, true);
      window.addEventListener("resize", this._onViewport);
      window.addEventListener("scroll", this._onViewport, true);
      this._build();
    }

    disconnectedCallback() {
      instances.splice(instances.indexOf(this), 1);
      document.removeEventListener("pointerdown", this._onPointerDown, true);
      document.removeEventListener("keydown", this._onKeyDown, true);
      window.removeEventListener("resize", this._onViewport);
      window.removeEventListener("scroll", this._onViewport, true);
      this._teardown();
    }

    attributeChangedCallback(name) {
      if (!this.isConnected || !this._bar) return;
      if (name === "theme") {
        this._post(this._frames(), { type: "theme", value: this._theme() });
        return;
      }
      this._teardown();
      this._build();
    }

    _theme() {
      return oneOf(THEMES, this.getAttribute("theme"));
    }

    _query(extra) {
      var query = new URLSearchParams({ origin: location.origin });
      var theme = this._theme();
      if (theme) query.set("theme", theme);
      for (var key in extra) if (extra[key]) query.set(key, extra[key]);
      return query.toString();
    }

    _build() {
      this._bar = frame(
        BASE + "bar.html?" + this._query({ hide: oneOf(HIDE, this.getAttribute("hide")) }),
        "Account",
        "allow-scripts allow-same-origin"
      );
      this._bar.setAttribute("data-empty", "");
      this._shadow.replaceChildren(this._bar);
      adopt(this._shadow, FRAME_CSS);

      // The panels live at the end of the body, outside the application's header, so no
      // transformed or clipping ancestor of the header can misplace or cut them.
      this._layer = document.createElement("div");
      var layer = this._layer.attachShadow({ mode: "closed" });
      adopt(layer, LAYER_CSS);
      this._layerRoot = layer;
      document.body.appendChild(this._layer);
    }

    _teardown() {
      this._open = null;
      this._panels = {};
      if (this._layer) this._layer.remove();
      this._layer = null;
      this._bar = null;
      this._shadow.replaceChildren();
    }

    _frames() {
      var list = this._bar ? [this._bar] : [];
      for (var key in this._panels) list.push(this._panels[key].element);
      return list;
    }

    owns(source) {
      return this._frames().some(function (element) {
        return element.contentWindow === source;
      });
    }

    _post(elements, message) {
      message.ns = NS;
      elements.forEach(function (element) {
        if (element.contentWindow) element.contentWindow.postMessage(message, ACCOUNTS);
      });
    }

    receive(source, data) {
      if (this._bar && source === this._bar.contentWindow) return this._fromBar(data);
      for (var key in this._panels) {
        if (this._panels[key].element.contentWindow === source) return this._fromPanel(key, data);
      }
    }

    _fromBar(data) {
      if (data.type === "size") {
        var width = Math.max(0, Math.ceil(Number(data.width) || 0));
        var height = Math.max(0, Math.ceil(Number(data.height) || 0));
        this._barInset = Math.max(0, Number(data.inset) || 0);
        this._bar.style.width = width + "px";
        this._bar.style.height = height + "px";
        if (width && height) this._bar.removeAttribute("data-empty");
        else this._bar.setAttribute("data-empty", "");
        this._place();
      } else if (data.type === "toggle" && oneOf(PANELS, data.panel)) {
        if (this._open === data.panel) this._close(false);
        else this._openPanel(data.panel);
      } else if (data.type === "signin") {
        navigateTop(this, "signin", safeHref(this.getAttribute("signin-href")) || ACCOUNTS + "/login");
      }
    }

    _fromPanel(panel, data) {
      var entry = this._panels[panel];
      if (data.type === "size") {
        entry.width = Math.max(0, Math.ceil(Number(data.width) || 0));
        entry.height = Math.max(0, Math.ceil(Number(data.height) || 0));
        entry.inset = Math.max(0, Number(data.inset) || 0);
        var first = !entry.ready;
        entry.ready = true;
        if (this._open === panel) {
          this._place();
          if (first) this._show(panel);
        }
      } else if (data.type === "close" && this._open === panel) {
        this._close(Boolean(data.focusBar));
      } else if (data.type === "signout" && panel === "account") {
        var href = safeHref(this.getAttribute("signout-href"));
        this._close(false);
        if (href) navigateTop(this, "signout", href);
      }
    }

    _openPanel(panel) {
      if (this._open) this._close(false);
      this._open = panel;
      var entry = this._panels[panel];
      if (!entry) {
        var extra = { panel: panel };
        if (panel === "account" && safeHref(this.getAttribute("signout-href"))) extra.signout = "1";
        entry = this._panels[panel] = {
          element: frame(
            BASE + "panel.html?" + this._query(extra),
            "Account",
            "allow-scripts allow-same-origin allow-popups allow-popups-to-escape-sandbox"
          ),
          ready: false,
          width: 352,
          height: 0,
          inset: 16,
        };
        this._layerRoot.appendChild(entry.element);
      }
      this._post([this._bar], { type: "state", open: panel, focus: false });
      this._place();
      if (entry.ready) this._show(panel);
    }

    _show(panel) {
      var entry = this._panels[panel];
      entry.element.setAttribute("data-shown", "");
      entry.element.focus();
      this._post([entry.element], { type: "opened", maxHeight: this._maxHeight() });
    }

    _close(focusBar) {
      var panel = this._open;
      if (!panel) return;
      this._open = null;
      var entry = this._panels[panel];
      if (entry) entry.element.removeAttribute("data-shown");
      if (focusBar && this._bar) this._bar.focus();
      this._post([this._bar], { type: "state", open: null, focus: focusBar });
    }

    _maxHeight() {
      var bar = this._bar.getBoundingClientRect();
      var top = bar.bottom - this._barInset + GAP;
      return Math.max(0, Math.floor(window.innerHeight - top - EDGE));
    }

    /** Puts the open panel under the bar, its end edge on the bar's, inside the screen. */
    _place() {
      var entry = this._open && this._panels[this._open];
      if (!entry || !this._bar) return;
      var bar = this._bar.getBoundingClientRect();
      var inset = this._barInset;
      var drawnWidth = Math.max(0, entry.width - 2 * entry.inset);
      var rtl = getComputedStyle(this).direction === "rtl";
      var left = rtl ? bar.left + inset : bar.right - inset - drawnWidth;
      left = Math.min(left, window.innerWidth - EDGE - drawnWidth);
      left = Math.max(left, EDGE);
      var top = bar.bottom - inset + GAP;
      var style = entry.element.style;
      style.left = left - entry.inset + "px";
      style.top = top - entry.inset + "px";
      style.width = entry.width + "px";
      // Until the panel says how tall it is, it may take what is free below the bar.
      style.height = (entry.ready ? entry.height : this._maxHeight() + 2 * entry.inset) + "px";
    }

    _onViewport() {
      if (!this._open || this._frame) return;
      var self = this;
      this._frame = requestAnimationFrame(function () {
        self._frame = 0;
        self._place();
      });
    }

    // A press anywhere on this page closes the panel. Presses inside the frames never
    // reach this document: the bar's buttons toggle through their messages, and the panel
    // closes itself on a press beside it.
    _onPointerDown() {
      if (this._open) this._close(false);
    }

    _onKeyDown(event) {
      if (event.key === "Escape" && this._open) this._close(true);
    }
  }

  window.addEventListener("message", function (event) {
    if (event.origin !== ACCOUNTS) return;
    var data = event.data;
    if (!data || typeof data !== "object" || data.ns !== NS || typeof data.type !== "string") return;
    for (var i = 0; i < instances.length; i++) {
      if (instances[i].owns(event.source)) {
        instances[i].receive(event.source, data);
        return;
      }
    }
  });

  window.customElements.define(ELEMENT, AccountBar);
})();
