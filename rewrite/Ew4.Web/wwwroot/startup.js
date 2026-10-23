'use strict';
// Keep this entry point parseable by WebViews that cannot parse the game scripts.
(function () {
  var complete = false, failed = false, timer, stage = '启动脚本';
  var match = navigator.userAgent.match(/(?:Chrome|Chromium)\/(\d+)/);
  var version = match ? match[1] : '未知';
  var minimum = window.Ew4FrontendTarget || 80;
  function fail(message) {
    if (complete || failed) return;
    failed = true;
    clearTimeout(timer);
    document.getElementById('startup-status').hidden = false;
    document.getElementById('startup-message').textContent = message + '\nWebView / Chromium：' + version +
      (match && Number(version) < minimum ? '\n系统网页组件过旧，请检查系统更新或应用市场，更新系统 WebView 后重开游戏。' : '');
    var retry = document.getElementById('startup-retry');
    retry.hidden = false;
    retry.onclick = function () { location.reload(); };
  }
  function pending(label) {
    stage = label;
    clearTimeout(timer);
    timer = setTimeout(function () { fail('载入超时：' + stage + '，可重试载入。'); }, 45000);
  }
  window.Ew4Startup = {
    pending: pending,
    stop: function () { clearTimeout(timer); },
    ready: function () { complete = true; clearTimeout(timer); }
  };
  window.addEventListener('error', function (event) {
    if (event.target && event.target.tagName === 'SCRIPT') fail('脚本载入失败：' + event.target.src.split('/').pop());
    else if (event.message) fail('启动失败：' + event.message + (event.filename ? '\n' + event.filename.split('/').pop() + ':' + event.lineno : ''));
  }, true);
  window.addEventListener('unhandledrejection', function (event) {
    fail('启动失败：' + (event.reason && event.reason.message || String(event.reason)));
  });
  if (!window.globalThis) window.globalThis = window;
  if (match && Number(version) < 80) {
    var nativeDecode = HTMLImageElement.prototype.decode;
    HTMLImageElement.prototype.decode = function () {
      var image = this;
      if (image.complete && image.naturalWidth) return Promise.resolve();
      if (nativeDecode) return nativeDecode.call(image).catch(function (error) {
        if (!image.complete || !image.naturalWidth) throw error;
      });
      return new Promise(function (resolve, reject) {
        function cleanup() { image.removeEventListener('load', loaded); image.removeEventListener('error', broken); }
        function loaded() { cleanup(); resolve(); }
        function broken() { cleanup(); reject(new Error('Image load failed: ' + image.src)); }
        image.addEventListener('load', loaded); image.addEventListener('error', broken);
        if (image.complete) { if (image.naturalWidth) loaded(); else broken(); }
      });
    };
  }
  if (!Object.fromEntries) Object.defineProperty(Object, 'fromEntries', { configurable: true, writable: true, value: function (entries) {
    var result = {};
    Array.from(entries).forEach(function (entry) {
      Object.defineProperty(result, entry[0], { value: entry[1], writable: true, enumerable: true, configurable: true });
    });
    return result;
  }});
  // Syntax is lowered by the shared build; platform methods need separate fallbacks.
  if (!Array.prototype.at) Object.defineProperty(Array.prototype, 'at', { configurable: true, writable: true, value: function (index) {
    var length = this.length, n = Number(index) || 0;
    n = n < 0 ? Math.ceil(n) : Math.floor(n);
    n = n < 0 ? length + n : n;
    return n < 0 || n >= length ? undefined : this[n];
  }});
  [Element.prototype, Document.prototype, DocumentFragment.prototype].forEach(function (prototype) {
    if (!prototype.replaceChildren) Object.defineProperty(prototype, 'replaceChildren', { configurable: true, writable: true, value: function () {
      var fragment = document.createDocumentFragment();
      for (var i = 0; i < arguments.length; i++) fragment.appendChild(arguments[i] instanceof Node ? arguments[i] : document.createTextNode(String(arguments[i])));
      while (this.firstChild) this.removeChild(this.firstChild);
      this.appendChild(fragment);
    }});
  });
  pending(stage);
  if (match && Number(version) < minimum) fail('当前系统网页组件不支持游戏启动脚本。');
})();
