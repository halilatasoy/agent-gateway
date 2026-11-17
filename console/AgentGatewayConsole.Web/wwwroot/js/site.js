/**
 * Ortak biçimlendirme ve arayüz yardımcıları. Metinler her zaman Messages üzerinden gelir.
 */
(function (root) {
    'use strict';

    const locale = () => (Messages.language() === 'en' ? 'en-US' : 'tr-TR');
    const noValue = () => Messages.get('Global:NoValue');

    function escapeHtml(value) {
        if (value === null || value === undefined) return '';
        return String(value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    const Format = {
        number(v) {
            return v === null || v === undefined ? noValue() : new Intl.NumberFormat(locale()).format(v);
        },
        compact(v) {
            return v === null || v === undefined ? noValue()
                : new Intl.NumberFormat(locale(), { notation: 'compact', maximumFractionDigits: 1 }).format(v);
        },
        percent(v) {
            return v === null || v === undefined ? noValue()
                : new Intl.NumberFormat(locale(), { maximumFractionDigits: 1 }).format(v) + '%';
        },
        dateTime(iso) {
            if (!iso) return noValue();
            return new Date(iso).toLocaleString(locale(), { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', second: '2-digit' });
        },
        time(date) {
            return (date instanceof Date ? date : new Date(date)).toLocaleTimeString(locale(), { hour: '2-digit', minute: '2-digit', second: '2-digit' });
        },
        bucketLabel(iso, range) {
            const d = new Date(iso);
            return range === 'Last7Days'
                ? d.toLocaleString(locale(), { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit' })
                : d.toLocaleTimeString(locale(), { hour: '2-digit', minute: '2-digit' });
        },
        duration(ms) {
            if (ms === null || ms === undefined) return noValue();
            return ms < 1000 ? ms + ' ms' : (ms / 1000).toLocaleString(locale(), { maximumFractionDigits: 1 }) + ' s';
        }
    };

    const OUTCOME_ICONS = { Reject: 'ban', Mask: 'eye-off', Allow: 'check', None: 'minus' };

    const Ui = {
        escapeHtml,

        /** Layout'taki SVG setinden ikon. */
        icon(name, cls) {
            return `<svg class="i${cls ? ' ' + cls : ''}" aria-hidden="true" focusable="false"><use href="#i-${name}"/></svg>`;
        },

        /** Guardrail kararı: renkli rozet değil, tek ikonlu metin. */
        outcomeState(outcome) {
            const o = OUTCOME_ICONS[outcome] ? outcome : 'None';
            return `<span class="state state--${o.toLowerCase()}">${Ui.icon(OUTCOME_ICONS[o])}${escapeHtml(Messages.get('Outcome:' + o))}</span>`;
        },

        protocolLabel(protocol) {
            const p = protocol || 'http';
            return escapeHtml(Messages.has('Protocol:' + p) ? Messages.get('Protocol:' + p) : p);
        },

        phaseLabel(phase) {
            return phase && Messages.has('Phase:' + phase) ? escapeHtml(Messages.get('Phase:' + phase)) : noValue();
        },

        statusCode(code) {
            if (code === null || code === undefined) return `<span class="muted">${noValue()}</span>`;
            return `<span class="status-code${code >= 400 ? ' status-code--error' : ''}">${escapeHtml(code)}</span>`;
        },

        /** Veri türü etiketleri (TcKimlik, Email…). */
        dataTypeChips(types) {
            if (!types || types.length === 0) return `<span class="muted">${noValue()}</span>`;
            return `<span class="chips">${types.map(t => `<span class="chip">${escapeHtml(Ui.dataTypeLabel(t))}</span>`).join('')}</span>`;
        },

        dataTypeLabel(type) {
            return Messages.has('DataType:' + type) ? Messages.get('DataType:' + type) : type;
        },

        /** JSON metnini güvenle basar; anahtar/değer/noktalama ayrı renkte (grafit üzerinde). */
        jsonHtml(text) {
            if (!text) return escapeHtml(noValue());
            const re = /("(?:\\u[a-fA-F0-9]{4}|\\[^u]|[^\\"])*")(\s*:)?|\b(true|false|null)\b|(-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)|([{}\[\],])/g;
            let out = '', last = 0, m;
            while ((m = re.exec(text)) !== null) {
                out += escapeHtml(text.slice(last, m.index));
                if (m[1]) out += m[2] ? `<span class="j-key">${escapeHtml(m[1])}</span><span class="j-punc">${escapeHtml(m[2])}</span>` : `<span class="j-str">${escapeHtml(m[1])}</span>`;
                else if (m[3]) out += `<span class="j-lit">${m[3]}</span>`;
                else if (m[4]) out += `<span class="j-num">${m[4]}</span>`;
                else out += `<span class="j-punc">${escapeHtml(m[5])}</span>`;
                last = re.lastIndex;
            }
            return out + escapeHtml(text.slice(last));
        },

        dataTableLanguage() {
            return {
                lengthMenu: Messages.get('DataTable:LengthMenu'),
                info: Messages.get('DataTable:Info'),
                infoEmpty: Messages.get('DataTable:InfoEmpty'),
                infoFiltered: Messages.get('DataTable:InfoFiltered'),
                zeroRecords: Messages.get('DataTable:ZeroRecords'),
                emptyTable: Messages.get('DataTable:EmptyTable'),
                processing: Messages.get('DataTable:Processing'),
                paginate: {
                    first: Messages.get('DataTable:First'),
                    previous: '‹',
                    next: '›',
                    last: Messages.get('DataTable:Last')
                },
                aria: {
                    paginate: {
                        previous: Messages.get('DataTable:Previous'),
                        next: Messages.get('DataTable:Next')
                    }
                }
            };
        },

        /** Hata bildirimi: sağ altta grafit şerit, 4 sn sonra kaybolur. */
        toastError(message) {
            const host = document.getElementById('toasts');
            if (!host) return;
            const el = document.createElement('div');
            el.className = 'toast';
            el.setAttribute('role', 'alert');
            el.innerHTML = Ui.icon('alert') + `<span>${escapeHtml(message)}</span>`;
            host.appendChild(el);
            setTimeout(() => {
                el.classList.add('is-leaving');
                setTimeout(() => el.remove(), 250);
            }, 4000);
        },

        /**
         * Tepsideki tuşları seçim grubu olarak bağlar (aria-pressed). Seçim değişince callback çağrılır;
         * başlangıçta seçili değeri döner.
         */
        bindTray(selector, onChange) {
            const $tray = $(selector);
            $tray.on('click', '[data-range]', function () {
                if (this.getAttribute('aria-pressed') === 'true') return;
                $tray.find('[data-range]').attr('aria-pressed', 'false');
                this.setAttribute('aria-pressed', 'true');
                onChange(this.dataset.range);
            });
            return $tray.find('[aria-pressed="true"]').data('range');
        }
    };

    root.Format = Format;
    root.Ui = Ui;
})(window);
