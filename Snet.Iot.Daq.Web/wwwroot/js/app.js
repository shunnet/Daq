window.snet = {
    pluginSettingsTab: {
        get: function () {
            try {
                var value = sessionStorage.getItem('snet-plugin-settings-tab');
                return value === null ? null : Number(value);
            } catch (e) { return null; }
        },
        set: function (tab) {
            try { sessionStorage.setItem('snet-plugin-settings-tab', String(tab)); } catch (e) { }
        }
    },
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
    /* 设备/用户列表 ⋯ 操作菜单：菜单脱离滚动容器，按视口锚定。
       菜单绝对定位于 .device-more 内，而容器（如 .snet-table-wrap）带 overflow：
       - 向下弹出在滚动视口底部被裁剪（视觉上像被下方卡片遮挡）；
       - 即使向上/向下翻转，视口内剩余空间不足时（如半个视口行数）仍会被裁。
       因此这里改为一律使用 position:fixed 按视口坐标摆放：
       fixed 不受祖先 overflow 裁剪（.card 无 backdrop-filter/transform，不含固定包含块），
       若上方空间不足则贴视口底边，避免越界。 */
    deviceMenu: {
        place: function (root) {
            var menu = root && root.querySelector('.device-more-menu');
            if (!menu) return;
            var toggle = menu.parentElement ? menu.parentElement.querySelector('.device-more-toggle') : null;
            if (!toggle) return;
            var tr = toggle.getBoundingClientRect();
            var w = menu.offsetWidth || 170;
            var h = menu.offsetHeight || 150;
            // 视口上下剩余空间：空间大的一侧弹出
            var downSpace = window.innerHeight - tr.bottom - 4;
            var upSpace = tr.top - 4;
            var useUp = upSpace > downSpace;
            // 用 Math.max/min 夹住视口边界，任何情况下菜单都完整可见
            var top = useUp
                ? Math.max(8, tr.top - h - 4)
                : Math.min(window.innerHeight - h - 8, tr.bottom + 4);
            var left = Math.min(Math.max(8, tr.right - w), Math.max(8, window.innerWidth - w - 8));
            menu.classList.remove('menu-down', 'menu-up');
            menu.style.position = 'fixed';
            menu.style.top = top + 'px';
            menu.style.left = left + 'px';
            menu.style.right = 'auto';
            menu.style.bottom = 'auto';
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
