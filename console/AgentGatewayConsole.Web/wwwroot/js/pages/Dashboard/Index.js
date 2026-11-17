/**
 * Koruma Özeti: defter (sayılar), zaman grafiği (veriye göre çizilen SVG) ve model/araç tabloları.
 * AJAX ile periyodik yenilenir; sayfa yenilenmez.
 */
$(async function () {
    await Messages.load(['Global', 'Dashboard', 'Outcome', 'Errors']);

    const ctx = window.PageContext;
    const esc = Ui.escapeHtml;
    const M = (key, ...args) => Messages.get(key, ...args);
    const chartEl = document.getElementById('timelineChart');
    let range = Ui.bindTray('#rangeTray', r => { range = r; load(); });
    let timeline = null;

    $('#refreshInfo').text(M('Dashboard:AutoRefresh', ctx.refreshSeconds));

    async function load() {
        try {
            const res = await $.getJSON(ctx.url.getData, { range });
            if (!res.success) {
                Ui.toastError(res.message);
                return;
            }
            render(res.data);
            // Şablondaki {0} yerine zaman damgası (eş aralıklı yazıyla) gelir
            const template = esc(M('Global:LastUpdated'));
            $('#lastUpdated').html(template.replace('{0}', `<time>${esc(Format.time(new Date()))}</time>`));
        } catch (e) {
            Ui.toastError(M('Errors:LoadFailed'));
        }
    }

    function render(data) {
        const s = data.summary;
        setKpi('rejected', Format.number(s.rejected));
        setKpi('masked', Format.number(s.masked));
        setKpi('totalRequests', Format.number(s.totalRequests));
        setKpi('llmRequests', Format.number(s.llmRequests));
        setKpi('mcpToolCalls', Format.number(s.mcpToolCalls));
        setKpi('tokens', Format.compact(s.inputTokens + s.outputTokens));
        setSub('tokens', M('Dashboard:TokensSub', Format.compact(s.inputTokens), Format.compact(s.outputTokens)));
        setKpi('failureRate', Format.percent(s.failureRate));
        setSub('failureRate', M('Dashboard:FailureRateSub', Format.number(s.failedRequests)));

        renderTimeline(data.timeline);
        renderRank('#modelTable', 'models', data.models, true);
        renderRank('#toolTable', 'tools', data.tools, false);
    }

    function setKpi(key, text) { $(`[data-kpi="${key}"]`).text(text); }
    function setSub(key, text) { $(`[data-kpi-sub="${key}"]`).text(text); }

    // ---- Zaman grafiği --------------------------------------------------------------------------

    /** Sıfırdan başlayan, 3–5 yatay çizgili "yuvarlak" ölçek. */
    function niceScale(max) {
        if (max <= 0) return { top: 4, step: 1 };
        const raw = max / 4;
        const mag = Math.pow(10, Math.floor(Math.log10(raw)));
        const n = raw / mag;
        const step = Math.max(1, Math.round((n <= 1 ? 1 : n <= 2 ? 2 : n <= 5 ? 5 : 10) * mag));
        return { top: Math.ceil(max / step) * step, step };
    }

    /**
     * Yığılmış çubuklar: tabanda engellenen (hata rengi), üstünde maskelenen (kobalt), en üstte
     * diğer istekler (açık kobalt). Çubuk yüksekliği toplam istek sayısıdır.
     */
    function renderTimeline(buckets) {
        timeline = buckets;
        const W = Math.max(280, Math.floor(chartEl.clientWidth));
        const H = Math.max(160, Math.floor(chartEl.clientHeight));
        const pad = { top: 8, right: 4, bottom: 28, left: 44 };
        const iw = W - pad.left - pad.right, ih = H - pad.top - pad.bottom;
        const n = buckets.length;
        const totals = buckets.reduce((t, b) => ({ req: t.req + b.requests, rej: t.rej + b.rejected, mask: t.mask + b.masked }), { req: 0, rej: 0, mask: 0 });
        const max = buckets.reduce((m, b) => Math.max(m, b.requests), 0);
        const { top, step } = niceScale(max);
        const y = v => pad.top + ih - (v / top) * ih;
        const slot = iw / Math.max(1, n);
        const barW = Math.max(2, Math.min(24, slot - Math.max(2, slot * 0.3)));
        const rangeName = $('#rangeTray [aria-pressed="true"]').text().trim();
        const aria = M('Dashboard:TimelineAria', rangeName, Format.number(totals.req), Format.number(totals.rej), Format.number(totals.mask));

        let svg = `<svg viewBox="0 0 ${W} ${H}" role="img" aria-label="${esc(aria)}">`;
        for (let v = 0; v <= top; v += step) {
            const yy = Math.round(y(v)) + 0.5;
            svg += `<line class="${v === 0 ? 'base-line' : 'grid-line'}" x1="${pad.left}" x2="${W - pad.right}" y1="${yy}" y2="${yy}"/>`;
            svg += `<text class="tick" x="${pad.left - 10}" y="${yy}" text-anchor="end" dominant-baseline="middle">${esc(Format.compact(v))}</text>`;
        }

        // Eksen etiketleri: sığdığı kadar, son kova her zaman etiketli
        const every = Math.ceil(n / Math.max(2, Math.floor(iw / 72)));
        buckets.forEach((b, i) => {
            const x0 = pad.left + slot * i;
            const cx = x0 + slot / 2;
            svg += `<rect class="col" data-i="${i}" x="${x0.toFixed(1)}" y="${pad.top}" width="${slot.toFixed(1)}" height="${ih}"/>`;
            const other = Math.max(0, b.requests - b.rejected - b.masked);
            let base = 0;
            [['bar-reject', b.rejected], ['bar-mask', b.masked], ['bar-other', other]].forEach(([cls, v]) => {
                if (v <= 0) return;
                const y1 = y(base + v), y0 = y(base);
                svg += `<rect class="${cls}" x="${(cx - barW / 2).toFixed(1)}" y="${y1.toFixed(1)}" width="${barW.toFixed(1)}" height="${Math.max(1, y0 - y1).toFixed(1)}"/>`;
                base += v;
            });
            if ((n - 1 - i) % every === 0) {
                svg += `<text class="tick" x="${cx.toFixed(1)}" y="${H - 8}" text-anchor="middle">${esc(Format.bucketLabel(b.startUtc, range))}</text>`;
            }
        });
        svg += '</svg>';

        chartEl.innerHTML = svg
            + '<div class="chart__tip" hidden></div>'
            + (max === 0 ? `<div class="chart__empty">${esc(M('Dashboard:NoData'))}</div>` : '');
    }

    function showTip(i) {
        const b = timeline[i];
        const col = chartEl.querySelector(`.col[data-i="${i}"]`);
        const tip = chartEl.querySelector('.chart__tip');
        if (!b || !col || !tip) return;

        chartEl.querySelectorAll('.col.is-on').forEach(c => c.classList.remove('is-on'));
        col.classList.add('is-on');

        const stepMs = timeline.length > 1 ? new Date(timeline[1].startUtc) - new Date(timeline[0].startUtc) : 0;
        const end = new Date(new Date(b.startUtc).getTime() + stepMs).toISOString();
        const other = Math.max(0, b.requests - b.rejected - b.masked);
        const row = (cls, key, v) => `<div class="chart__tip-row"><span class="swatch swatch--${cls}"></span><span>${esc(M(key))}</span><b>${esc(Format.number(v))}</b></div>`;
        tip.innerHTML =
            `<div class="chart__tip-time">${esc(Format.bucketLabel(b.startUtc, range))} – ${esc(Format.bucketLabel(end, range))}</div>`
            + row('reject', 'Dashboard:SeriesRejected', b.rejected)
            + row('mask', 'Dashboard:SeriesMasked', b.masked)
            + row('other', 'Dashboard:SeriesOther', other)
            + `<div class="chart__tip-row chart__tip-row--total"><span></span><span>${esc(M('Dashboard:TotalRequests'))}</span><b>${esc(Format.number(b.requests))}</b></div>`;
        tip.hidden = false;

        // Balon çubuğun üstünü kapatmasın: sütunun sağına, sığmazsa soluna yerleşir
        const colX = parseFloat(col.getAttribute('x'));
        const colW = parseFloat(col.getAttribute('width'));
        const gap = 8;
        const right = colX + colW + gap;
        tip.style.left = (right + tip.offsetWidth <= chartEl.clientWidth
            ? right
            : Math.max(0, colX - gap - tip.offsetWidth)) + 'px';
    }

    function hideTip() {
        const tip = chartEl.querySelector('.chart__tip');
        if (tip) tip.hidden = true;
        chartEl.querySelectorAll('.col.is-on').forEach(c => c.classList.remove('is-on'));
    }

    chartEl.addEventListener('pointermove', e => {
        const col = e.target.closest && e.target.closest('.col');
        if (col && timeline) showTip(Number(col.dataset.i)); else hideTip();
    });
    chartEl.addEventListener('pointerleave', hideTip);

    // Genişlik değişince yeniden çiz (yükseklik sabit, döngü oluşmaz)
    let resizeFrame = 0;
    new ResizeObserver(() => {
        cancelAnimationFrame(resizeFrame);
        resizeFrame = requestAnimationFrame(() => { if (timeline) renderTimeline(timeline); });
    }).observe(chartEl);

    // ---- Sıralı tablolar ------------------------------------------------------------------------

    function renderRank(tableSel, key, items, withTokens) {
        const empty = !items || items.length === 0;
        $(`[data-empty="${key}"]`).prop('hidden', !empty);
        $(tableSel).prop('hidden', empty);
        const $body = $(tableSel + ' tbody');
        if (empty) {
            $body.empty();
            $(`[data-meta="${key}"]`).text('');
            return;
        }

        const total = items.reduce((s, i) => s + i.count, 0);
        const max = items.reduce((m, i) => Math.max(m, i.count), 0) || 1;
        $(`[data-meta="${key}"]`).text(M(key === 'models' ? 'Dashboard:ModelsMeta' : 'Dashboard:ToolsMeta', Format.number(items.length), Format.number(total)));

        $body.html(items.map(i => `
            <tr>
                <td class="rank__name" title="${esc(i.name)}">${esc(i.name)}</td>
                <td class="rank__bar"><span class="rank__track"><span class="rank__fill" data-share="${i.count / max}"></span></span></td>
                <td class="r">${esc(Format.number(i.count))}</td>
                ${withTokens ? `<td class="r rank__soft">${esc(i.tokens ? Format.compact(i.tokens) : M('Global:NoValue'))}</td>` : ''}
            </tr>`).join(''));
        $body.find('.rank__fill').each(function () {
            this.style.width = (Number(this.dataset.share) * 100).toFixed(1) + '%';
        });
    }

    await load();
    setInterval(load, Math.max(5, ctx.refreshSeconds) * 1000);
});
