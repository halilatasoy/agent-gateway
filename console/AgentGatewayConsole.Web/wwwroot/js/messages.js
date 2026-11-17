/**
 * Messages — i18n istemci yardımcısı (webds messages.js'in sade hâli).
 * /api/messages'tan kategori bazlı mesajları yükler ve önbelleğe alır.
 *
 *   await Messages.load(['Global', 'DataTable']);
 *   Messages.get('Dashboard:TokensSub', girdi, cikti);   // {0}, {1} yer tutucuları
 */
(function (root) {
    'use strict';

    const _cache = {};
    let _language = null;

    async function load(categories) {
        if (!Array.isArray(categories) || categories.length === 0) return;
        const toLoad = categories.filter(c => !(c in _cache));
        if (toLoad.length === 0) return;
        try {
            const resp = await fetch('/api/messages?cats=' + encodeURIComponent(toLoad.join(',')), { credentials: 'same-origin' });
            if (!resp.ok) throw new Error('HTTP ' + resp.status);
            const data = await resp.json();
            _language = data.language || _language;
            Object.assign(_cache, data.categories || {});
        } catch (err) {
            console.warn('[Messages] yükleme hatası:', err);
        }
    }

    function get(key, ...args) {
        if (!key || typeof key !== 'string') return key;
        let value;
        const sep = key.indexOf(':');
        if (sep > 0) {
            const cat = key.substring(0, sep);
            const k = key.substring(sep + 1);
            value = _cache[cat] && _cache[cat][k];
        }
        if (value === undefined || value === null) value = '[' + key + ']';
        for (let i = 0; i < args.length; i++) {
            value = value.replace('{' + i + '}', args[i]);
        }
        return value;
    }

    function has(key) {
        const sep = key ? key.indexOf(':') : -1;
        if (sep <= 0) return false;
        const cat = key.substring(0, sep);
        return !!(_cache[cat] && _cache[cat][key.substring(sep + 1)] !== undefined);
    }

    function language() {
        return _language || document.documentElement.lang || 'tr';
    }

    root.Messages = { load, get, has, language };
})(window);
