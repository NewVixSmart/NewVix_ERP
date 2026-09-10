// Silk.Trading.Web - site-wide scripts

(function () {
    'use strict';

    // Bootstrap-icons used purely decoratively across the app
    document.querySelectorAll('i.bi').forEach(function (icon) {
        icon.setAttribute('aria-hidden', 'true');
    });

    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    var csrfToken = tokenInput ? tokenInput.value : '';

    function postForm(url, data) {
        var body = new URLSearchParams(data);
        return fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
                'RequestVerificationToken': csrfToken,
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: body.toString()
        });
    }

    function handleResult(res, errorMsg) {
        if (res.ok) {
            location.reload();
        } else {
            res.text().then(function (text) { alert(text || errorMsg); });
        }
    }

    // Accessible edit-name modal (replaces prompt())
    var editModal = null;
    var editTarget = null;

    function ensureEditModal() {
        if (editModal) return;

        var div = document.createElement('div');
        div.className = 'modal fade';
        div.id = 'editNameModal';
        div.setAttribute('tabindex', '-1');
        div.setAttribute('role', 'dialog');
        div.setAttribute('aria-modal', 'true');
        div.setAttribute('aria-hidden', 'true');
        div.innerHTML = '' +
            '<div class="modal-dialog">' +
            '  <form id="editNameForm" class="modal-content" novalidate>' +
            '    <div class="modal-header">' +
            '      <h5 class="modal-title" id="editNameTitle">تعديل الاسم</h5>' +
            '      <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="إغلاق"></button>' +
            '    </div>' +
            '    <div class="modal-body">' +
            '      <label for="editNameInput" class="form-label">الاسم الجديد</label>' +
            '      <input type="text" id="editNameInput" class="form-control" required maxlength="200" />' +
            '    </div>' +
            '    <div class="modal-footer">' +
            '      <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">إلغاء</button>' +
            '      <button type="submit" class="btn btn-primary"><i class="bi bi-check-lg"></i> حفظ</button>' +
            '    </div>' +
            '  </form>' +
            '</div>';

        document.body.appendChild(div);

        div.querySelector('#editNameForm').addEventListener('submit', function (e) {
            e.preventDefault();
            var input = div.querySelector('#editNameInput');
            var val = input.value.trim();
            if (!val || !editTarget) return;
            postForm('/' + editTarget.endpoint + '/Edit', { Id: editTarget.id, Name: val })
                .then(function (res) {
                    if (res.ok) {
                        editModal.hide();
                        location.reload();
                    } else {
                        res.text().then(function (text) { alert(text || 'تعذر تعديل العنصر'); });
                    }
                });
        });

        div.querySelector('#editNameModal').addEventListener('hidden.bs.modal', function () {
            var input = div.querySelector('#editNameInput');
            if (input) input.value = '';
        });

        editModal = new bootstrap.Modal(div, {});
    }

    // Manage categories & item types rows (edit / delete)
    ['categoriesTable', 'itemTypesTable'].forEach(function (tableId) {
        var table = document.getElementById(tableId);
        if (!table) return;

        var endpoint = tableId === 'categoriesTable' ? 'Categories' : 'ItemTypes';

        table.addEventListener('click', function (e) {
            var editBtn = e.target.closest('.btn-edit');
            var delBtn = e.target.closest('.btn-del');

            if (editBtn) {
                var nameTd = editBtn.closest('tr').querySelector('td[data-name]');
                var current = nameTd ? nameTd.dataset.name : '';
                ensureEditModal();
                editTarget = { id: editBtn.dataset.id, endpoint: endpoint };
                var input = document.getElementById('editNameInput');
                input.value = current;
                editModal.show();
                input.focus();
                input.select();
            }

            if (delBtn) {
                if (confirm('هل أنت متأكد من الحذف؟')) {
                    postForm('/' + endpoint + '/Delete?id=' + delBtn.dataset.id, {})
                        .then(function (res) { handleResult(res, 'لا يمكن حذف العنصر'); });
                }
            }
        });
    });
})();