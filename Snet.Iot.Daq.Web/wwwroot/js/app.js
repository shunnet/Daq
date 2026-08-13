window.snet = {
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
                    dotnetRef.invokeMethodAsync('CloseAllActions');
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
    /* 日志自动跟随滚动：新日志到达时滚到底部；用户上滚回溯时暂停跟随 */
    logScroll: {
        _auto: true,
        init: function () { this._auto = true; },
        stick: function (el) {
            if (this._auto && el) el.scrollTop = el.scrollHeight;
        },
        onScroll: function (el) {
            // 距底 < 60px 视为已回到底部，恢复自动跟随
            this._auto = el.scrollHeight - (el.scrollTop + el.clientHeight) < 60;
        }
    }
};
