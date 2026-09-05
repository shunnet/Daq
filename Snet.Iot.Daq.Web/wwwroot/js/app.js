window.snet = {
    modalFocus: {
        entries: new Map(),
        nextId: 0,
        open: function (el) {
            var id = String(++this.nextId);
            var previous = document.activeElement;
            var candidates = function () {
                return Array.from(el.querySelectorAll('button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href], [tabindex="0"]'))
                    .filter(function (node) { return node.getClientRects().length > 0; });
            };
            var handler = function (event) {
                if (event.key !== 'Tab') return;
                var items = candidates();
                if (!items.length) { event.preventDefault(); el.focus(); return; }
                var first = items[0], last = items[items.length - 1];
                if (event.shiftKey && (document.activeElement === first || document.activeElement === el)) {
                    event.preventDefault(); last.focus();
                } else if (!event.shiftKey && document.activeElement === last) {
                    event.preventDefault(); first.focus();
                }
            };
            el.addEventListener('keydown', handler);
            this.entries.set(id, { element: el, previous: previous, handler: handler });
            (candidates()[0] || el).focus();
            return id;
        },
        close: function (id) {
            var entry = this.entries.get(id);
            if (!entry) return;
            entry.element.removeEventListener('keydown', entry.handler);
            this.entries.delete(id);
            if (entry.previous && entry.previous.isConnected) entry.previous.focus();
        }
    },
    /* 外部点击关闭：点击 .tree-actions 区域外任意处 → 回调全部注册方收起（移出不关闭，区域外点击才关闭） */
    outsideClick: {
        handlers: [],
        register: function (dotnetRef) {
            // 打开新菜单前清掉旧菜单残留 handler（菜单互斥已先收起旧菜单，其 handler 必须移除，
            // 否则旧 handler 在下次区域外点击时 unregisterAll 会把新菜单的 handler 一并清掉，导致新菜单关不掉）
            snet.outsideClick.unregisterAll();
            var fn = function (e) {
                if (!e.target.closest('.tree-actions, .device-more')) {
                    snet.outsideClick.unregisterAll();
                    // 组件可能已随切页释放：吞掉回调失败，避免 Unhandled Promise Rejection 刷屏
                    try {
                        var invoke = dotnetRef.invokeMethodAsync('CloseAllActions');
                        if (invoke && invoke.catch) invoke.catch(function () { });
                    } catch (err) { }
                }
            };
            document.addEventListener('click', fn);
            snet.outsideClick.handlers.push(fn);
        },
        unregisterAll: function () {
            snet.outsideClick.handlers.forEach(function (fn) { document.removeEventListener('click', fn); });
            snet.outsideClick.handlers = [];
        }
    },
    clickFileInput: function () {
        var input = document.querySelector('input.upload-input-hidden');
        if (input) input.click();
    },
    download: function (filename, content) {
        var blob = new Blob([content], { type: 'application/json;charset=utf-8' });
        var url = URL.createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    },
    setTheme: function (dark) {
        var theme = dark ? 'dark' : 'light';
        document.documentElement.setAttribute('data-theme', theme);
        try { localStorage.setItem('snet-theme', theme); } catch (e) { }
    },
    getTheme: function () {
        return document.documentElement.getAttribute('data-theme') || 'dark';
    },
    setLang: function (lang) {
        document.documentElement.setAttribute('lang', lang);
        try { localStorage.setItem('snet-lang', lang); } catch (e) { }
    },
    getLang: function () {
        try { return localStorage.getItem('snet-lang') || ''; } catch (e) { return ''; }
    },
    /* 日志自动跟随滚动：新日志到达时滚到底部；用户上滚回溯时暂停跟随 */
    logScroll: {
        init: function (el) {
            if (!el || el._snetLogScroll) return;
            el._snetLogAuto = true;
            el._snetLogScroll = function () {
                el._snetLogAuto = el.scrollHeight - (el.scrollTop + el.clientHeight) < 60;
            };
            el.addEventListener('scroll', el._snetLogScroll, { passive: true });
        },
        stick: function (el) {
            if (el && el._snetLogAuto) el.scrollTop = el.scrollHeight;
        }
    }
};
