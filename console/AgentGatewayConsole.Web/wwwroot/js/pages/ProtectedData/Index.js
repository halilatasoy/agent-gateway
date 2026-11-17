/**
 * Yakalanan Hassas Veriler: guardrail'in engellediği/maskelediği istekler + inceleme paneli.
 * İnceleme panelinde gateway'e gelen isteğin maskelenmiş akışı adım adım gösterilir: kullanıcı istemi,
 * modelin araç çağrıları, araç sonuçları, hassas verinin bulunduğu adım ve en sonda gateway'in kararı.
 */
$(async function () {
    await Messages.load(['Global', 'ProtectedData', 'DataType', 'Outcome', 'Phase', 'Protocol', 'DataTable', 'Errors']);

    const ctx = window.PageContext;
    const esc = Ui.escapeHtml;
    const M = (key, ...args) => Messages.get(key, ...args);
    const dialog = document.getElementById('detailDialog');
    const listUrl = () => ctx.url.getList + '?range=' + encodeURIComponent(range);
    let range = Ui.bindTray('#rangeTray', r => { range = r; table.ajax.url(listUrl()).load(); });
    let activeRow = null;

    const ROLE_ICONS = { user: 'user', assistant: 'bot', tool: 'wrench', system: 'file' };
    const ROLE_KEYS = { user: 'RoleUser', assistant: 'RoleAssistant', tool: 'RoleTool', system: 'RoleSystem' };

    const table = $('#protectedTable').DataTable({
        language: Ui.dataTableLanguage(),
        pageLength: 25,
        lengthMenu: [[25, 50, 100], [25, 50, 100]],
        order: [[0, 'desc']],
        autoWidth: false,
        dom: 't<"dt-foot"<"dt-foot-left"li><"dt-foot-right"p>>',
        ajax: {
            url: listUrl(),
            dataSrc: function (json) {
                if (json.success === false) Ui.toastError(json.message);
                return json.data || [];
            }
        },
        createdRow: function (row) { row.tabIndex = 0; },
        columns: [
            { data: 'occurredAtUtc', className: 'mono mono--ink', render: (d, type) => type === 'display' ? esc(Format.dateTime(d)) : d },
            { data: 'outcome', render: d => Ui.outcomeState(d) },
            { data: 'dataTypes', orderable: false, render: d => Ui.dataTypeChips(d) },
            { data: 'phase', render: d => Ui.phaseLabel(d) },
            { data: 'protocol', render: d => Ui.protocolLabel(d) },
            { data: null, className: 'mono', render: r => esc(r.model || r.toolName || M('Global:NoValue')) },
            { data: 'clientAddress', className: 'mono', render: d => esc(d || M('Global:NoValue')) },
            { data: 'statusCode', render: d => Ui.statusCode(d) },
            { data: 'durationMs', className: 'mono r', render: (d, type) => type === 'display' ? esc(Format.duration(d)) : d },
            { data: 'id', orderable: false, className: 'cell-go', render: () => Ui.icon('chevron') }
        ]
    });

    // Satır: tıklama veya Enter/Boşluk ile açılır
    $('#protectedTable tbody')
        .on('click', 'tr', function () { openRow(this); })
        .on('keydown', 'tr', function (e) {
            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                openRow(this);
            }
        });

    function openRow(tr) {
        const row = table.row(tr).data();
        if (!row) return;
        $('#protectedTable tbody tr.is-on').removeClass('is-on');
        tr.classList.add('is-on');
        activeRow = tr;
        showDetail(row.id);
    }

    // İnceleme paneli: kapat düğmesi, arka plan tıklaması, Esc (yerel)
    dialog.querySelector('[data-close]').addEventListener('click', () => dialog.close());
    dialog.addEventListener('click', e => { if (e.target === dialog) dialog.close(); });
    dialog.addEventListener('close', () => { if (activeRow && document.contains(activeRow)) activeRow.focus(); });

    // ---- Yardımcılar -------------------------------------------------------------------------

    /** Maskelenmiş metni güvenle basar; <ETIKET> yer tutucularını vurgular. */
    function highlight(text) {
        return esc(text).replace(/&lt;([A-Z_]{2,40})&gt;/g, '<mark class="redacted">&lt;$1&gt;</mark>');
    }

    function phaseText(phase) {
        return phase && Messages.has('Phase:' + phase) ? M('Phase:' + phase) : null;
    }

    function locationLabel(f) {
        const tool = f.toolName ? ' · ' + f.toolName : '';
        switch (f.source) {
            case 'user': return M('ProtectedData:RoleUser');
            case 'assistant': return M('ProtectedData:RoleAssistant');
            case 'tool': return M('ProtectedData:RoleTool') + tool;
            case 'system': return M('ProtectedData:RoleSystem');
            case 'toolCall': return M('ProtectedData:ToolCallArgs') + tool;
            case 'responseToolCall': return M('ProtectedData:NextStep') + tool;
            case 'completion': return M('ProtectedData:Completion');
            default: return M('ProtectedData:Body');
        }
    }

    function note(key, ...args) {
        return `<div class="note">${Ui.icon('info')}<span>${esc(M(key, ...args))}</span></div>`;
    }

    function section(titleKey, inner, hint) {
        return `<section class="section">
            <div class="section__head">
                <h3 class="section__title">${esc(M(titleKey))}</h3>
                ${hint ? `<p class="section__hint">${esc(hint)}</p>` : ''}
            </div>
            ${inner}
        </section>`;
    }

    function renderFindings(findings) {
        return `<ul class="findings">${findings.map(f => `
            <li class="finding">
                <div class="finding__head">
                    <span class="chip">${esc(Ui.dataTypeLabel(f.type))}</span>
                    <span class="finding__count">×${esc(f.count)}</span>
                    <span class="finding__where">${Ui.icon('pin')}${esc(locationLabel(f))}</span>
                </div>
                ${f.snippet ? `<p class="finding__snippet">${highlight(f.snippet)}</p>` : ''}
            </li>`).join('')}</ul>`;
    }

    function renderCalls(calls) {
        if (!calls || calls.length === 0) return '';
        return `<ul class="calls">${calls.map(c => `
            <li class="call">
                <span class="call__head">${Ui.icon('corner')}<span class="call__label">${esc(M('ProtectedData:CalledTool'))}</span><code class="call__name">${esc(c.name || '?')}</code></span>
                ${c.arguments ? `<code class="call__args">${highlight(c.arguments)}</code>` : ''}
            </li>`).join('')}</ul>`;
    }

    function flagHtml(findings) {
        if (!findings || findings.length === 0) return '';
        return `<span class="flow__flag">${Ui.icon('alert')}${esc(M('ProtectedData:FoundHere'))}
            <span class="chips">${findings.map(f => `<span class="chip chip--flag">${esc(Ui.dataTypeLabel(f.type))} ×${esc(f.count)}</span>`).join('')}</span></span>`;
    }

    /** Akıştaki tek adım: numaralı düğüm, rol başlığı, varsa bulgu işareti ve içerik. */
    function step({ node, role, title, sub, findings, body, cls }) {
        const flagged = findings && findings.length > 0;
        return `
            <li class="flow__step flow__step--${esc(role)}${flagged ? ' is-flagged' : ''}${cls ? ' ' + cls : ''}">
                <span class="flow__node" aria-hidden="true">${node}</span>
                <div class="flow__main">
                    <div class="flow__head">
                        <span class="flow__role">${Ui.icon(ROLE_ICONS[role] || 'gate')}${esc(title)}${sub ? `<code>${esc(sub)}</code>` : ''}</span>
                        ${flagHtml(findings)}
                    </div>
                    ${body ? `<div class="flow__body">${body}</div>` : ''}
                </div>
            </li>`;
    }

    function messageStep(m) {
        let body = '';
        if (m.contentOmitted) {
            body += `<p class="flow__meta">${esc(M('ProtectedData:SystemOmitted', Format.number(m.contentLength)))}</p>`;
        } else if (m.content) {
            body += `<pre class="flow__text">${highlight(m.content)}</pre>`;
            if (m.truncated) body += `<p class="flow__meta">${esc(M('ProtectedData:Truncated', Format.number(m.contentLength)))}</p>`;
        }
        body += renderCalls(m.toolCalls);
        return step({
            node: esc(String(m.index + 1).padStart(2, '0')),
            role: m.role,
            title: M('ProtectedData:' + (ROLE_KEYS[m.role] || 'RoleAssistant')),
            sub: m.role === 'tool' ? m.toolName : null,
            findings: m.findings,
            body
        });
    }

    function decisionStep(d) {
        const o = d.outcome || 'None';
        const phase = phaseText(d.phase);
        return `
            <li class="flow__step flow__step--decision is-${esc(o.toLowerCase())}">
                <span class="flow__node" aria-hidden="true">${Ui.icon('gate')}</span>
                <div class="flow__main">
                    <div class="flow__head">
                        <span class="flow__role">${esc(M('ProtectedData:GatewayDecision'))}</span>
                        ${Ui.outcomeState(o)}
                        ${phase ? `<span class="flow__phase">${esc(M('ProtectedData:AtPhase', phase))}</span>` : ''}
                    </div>
                </div>
            </li>`;
    }

    function renderContent(c, d) {
        if (!c) return section('ProtectedData:Flow', note('ProtectedData:NoContent'));
        if (c.redactionFailed) return section('ProtectedData:Flow', note('ProtectedData:RedactionFailed'));

        const findings = c.findings || [];
        let html = section('ProtectedData:CapturedData', findings.length === 0 ? note('ProtectedData:NoFindings') : renderFindings(findings));

        if (c.parseFailed) {
            return html + section('ProtectedData:Flow', note('ProtectedData:ParseFailed'));
        }

        let steps = (c.messages || []).map(messageStep).join('');
        let next = (c.messages || []).reduce((max, m) => Math.max(max, m.index + 1), 0) + 1;
        const nodeNo = () => esc(String(next++).padStart(2, '0'));

        // Yanıt aşaması: modelin istediği sonraki adım ve model yanıtı da akışın parçası
        if (c.responseToolCalls && c.responseToolCalls.length > 0) {
            steps += step({
                node: nodeNo(), role: 'assistant', title: M('ProtectedData:NextStep'),
                findings: findings.filter(f => f.source === 'responseToolCall'),
                body: renderCalls(c.responseToolCalls)
            });
        }
        if (c.completion && c.completion.length > 0) {
            steps += step({
                node: nodeNo(), role: 'assistant', title: M('ProtectedData:Completion'),
                findings: findings.filter(f => f.source === 'completion'),
                body: c.completion.map(t => `<pre class="flow__text">${highlight(t)}</pre>`).join('')
            });
        }
        steps += decisionStep(d);

        const omitted = c.omittedMessages > 0 ? note('ProtectedData:OmittedMessages', Format.number(c.omittedMessages)) : '';
        return html + section('ProtectedData:Flow', omitted + `<ol class="flow">${steps}</ol>`, M('ProtectedData:FlowHint'));
    }

    function renderFacts(d) {
        const nv = M('Global:NoValue');
        const rows = [
            ['ProtectedData:Phase', esc(phaseText(d.phase) || nv)],
            ['ProtectedData:Protocol', Ui.protocolLabel(d.protocol)],
            ['ProtectedData:Path', esc([d.httpMethod, d.path].filter(Boolean).join(' ') || nv)],
            ['ProtectedData:Status', Ui.statusCode(d.statusCode)],
            ['ProtectedData:Model', esc(d.requestModel || nv)],
            ['ProtectedData:Tool', esc([d.mcpTarget, d.toolName].filter(Boolean).join(' / ') || nv)],
            ['ProtectedData:Tokens', esc(`${Format.number(d.inputTokens)} / ${Format.number(d.outputTokens)}`)],
            ['ProtectedData:Duration', esc(Format.duration(d.durationMs))],
            ['ProtectedData:Client', esc(d.clientAddress || nv)],
            ['ProtectedData:Gateway', esc(d.gateway || nv)],
            ['ProtectedData:Reason', esc(d.reason || nv)],
            ['ProtectedData:Error', esc(d.error || nv)]
        ];
        return rows.map(([k, v]) => `<div><dt>${esc(M(k))}</dt><dd>${v}</dd></div>`).join('');
    }

    async function showDetail(id) {
        try {
            const res = await $.getJSON(ctx.url.getDetail, { id });
            if (!res.success) {
                Ui.toastError(res.message);
                return;
            }
            const d = res.data;
            const phase = phaseText(d.phase);

            $('#detailEyebrow').html(`${esc(M('ProtectedData:DetailTitle'))} <code>#${esc(d.id)}</code>`);
            $('#detailTitle').html(Ui.outcomeState(d.outcome));
            $('#detailMeta').html([
                d.dataTypes && d.dataTypes.length ? Ui.dataTypeChips(d.dataTypes) : '',
                `<time>${esc(Format.dateTime(d.occurredAtUtc))}</time>`,
                phase ? `<span>${esc(M('ProtectedData:AtPhase', phase))}</span>` : '',
                d.requestModel || d.toolName ? `<code>${esc(d.requestModel || d.toolName)}</code>` : ''
            ].filter(Boolean).join(''));
            $('#detailContent').html(renderContent(d.content, d));
            $('#detailFacts').html(renderFacts(d));
            $('#detailGuardrails').html(Ui.jsonHtml(d.guardrails));
            $('#detailAttributes').html(Ui.jsonHtml(d.attributes));

            dialog.querySelector('.disclosure').open = false;
            dialog.querySelector('.inspector__body').scrollTop = 0;
            if (!dialog.open) dialog.showModal();
        } catch (e) {
            Ui.toastError(M('Errors:LoadFailed'));
        }
    }
});
