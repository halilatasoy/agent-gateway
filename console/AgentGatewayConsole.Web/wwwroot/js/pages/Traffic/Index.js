/**
 * Trafik Kaydı: tüm istekler, DataTables server-side sayfalama ve filtreler.
 */
$(async function () {
    await Messages.load(['Global', 'Traffic', 'Outcome', 'Protocol', 'DataTable', 'Errors']);

    const ctx = window.PageContext;
    const esc = Ui.escapeHtml;
    const nv = () => Messages.get('Global:NoValue');

    const table = $('#trafficTable').DataTable({
        language: Ui.dataTableLanguage(),
        serverSide: true,
        processing: true,
        searching: true,
        pageLength: 25,
        lengthMenu: [[25, 50, 100], [25, 50, 100]],
        order: [[0, 'desc']],
        autoWidth: false,
        dom: 't<"dt-foot"<"dt-foot-left"li><"dt-foot-right"p>>r',
        ajax: {
            url: ctx.url.getPage,
            data: function (d) {
                d.protocol = $('#filterProtocol').val();
                d.statusClass = $('#filterStatus').val();
                d.outcome = $('#filterOutcome').val();
                d.model = $('#filterModel').val();
            },
            dataSrc: function (json) {
                if (json.error) Ui.toastError(json.error);
                return json.data || [];
            }
        },
        columns: [
            { data: 'occurredAtUtc', className: 'mono mono--ink', render: d => esc(Format.dateTime(d)) },
            { data: 'protocol', render: d => Ui.protocolLabel(d) },
            { data: 'target', orderable: false, className: 'mono mono--ink cell-target', render: d => esc(d || nv()) },
            { data: 'httpMethod', orderable: false, className: 'mono', render: d => esc(d || nv()) },
            { data: 'statusCode', render: d => Ui.statusCode(d) },
            { data: 'outcome', orderable: false, render: d => Ui.outcomeState(d) },
            { data: 'durationMs', className: 'mono r', render: d => esc(Format.duration(d)) },
            { data: 'tokens', orderable: false, className: 'mono r', render: d => d ? esc(Format.number(d)) : `<span class="muted">${nv()}</span>` },
            { data: 'clientAddress', orderable: false, className: 'mono', render: d => esc(d || nv()) }
        ]
    });

    $('#filterProtocol, #filterStatus, #filterOutcome, #filterModel').on('change', () => table.ajax.reload());

    let searchTimer = null;
    $('#searchBox').on('input', function () {
        clearTimeout(searchTimer);
        const value = this.value;
        searchTimer = setTimeout(() => table.search(value).draw(), 350);
    });

    try {
        const res = await $.getJSON(ctx.url.getFilterOptions);
        if (res.success) {
            const $model = $('#filterModel');
            res.data.models.forEach(m => $model.append($('<option>').val(m).text(m)));
        }
    } catch (e) {
        Ui.toastError(Messages.get('Errors:LoadFailed'));
    }
});
