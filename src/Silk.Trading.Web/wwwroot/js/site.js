// Silk.Trading.Web - site-wide scripts
(function () {
    'use strict';

    // Bootstrap-icons used purely decoratively across the app
    document.querySelectorAll('i.bi').forEach(function (icon) {
        if (icon.classList.contains('visually-hidden')) return;
        if (icon.getAttribute('aria-hidden') === null) {
            icon.setAttribute('aria-hidden', 'true');
        }
    });

    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    var csrfToken = tokenInput ? tokenInput.value : '';

    // ---------- Accessible sidebar (mobile off-canvas) ----------
    var sidebar = document.getElementById('sidebar');
    var overlay = document.getElementById('mobileOverlay');
    var toggle = document.getElementById('sidebarToggle');
    var lastFocus = null;

    function isResponsive() {
        return window.innerWidth <= 991.98;
    }

    function setSidebarOpen(open) {
        if (!sidebar || !isResponsive()) {
            if (sidebar && !open) sidebar.classList.remove('show');
            if (overlay) overlay.classList.remove('show');
            return;
        }
        sidebar.classList.toggle('show', open);
        overlay.classList.toggle('show', open);
        if (toggle) toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        document.body.style.overflow = open ? 'hidden' : '';
    }

    window.toggleSidebar = function () {
        var willOpen = sidebar ? !sidebar.classList.contains('show') : false;
        if (willOpen) {
            lastFocus = document.activeElement;
            setSidebarOpen(true);
            var firstLink = sidebar.querySelector('.nav-link');
            if (firstLink) firstLink.focus();
        } else {
            setSidebarOpen(false);
            if (lastFocus && lastFocus.focus) lastFocus.focus();
        }
    };

    if (toggle && overlay && sidebar) {
        toggle.addEventListener('click', window.toggleSidebar);
        overlay.addEventListener('click', function () { setSidebarOpen(false); });
        sidebar.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && sidebar.classList.contains('show')) {
                window.toggleSidebar();
                if (toggle) toggle.focus();
            }
        });
        toggle.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                setSidebarOpen(false);
                if (toggle) toggle.focus();
            }
        });
        // Close after navigating
        sidebar.addEventListener('click', function (e) {
            if (e.target.closest('.nav-link')) setSidebarOpen(false);
        });
        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Tab') return;
            if (!sidebar.classList.contains('show')) return;
            var focusables = sidebar.querySelectorAll('a[href], button:not([disabled]), input:not([disabled])');
            if (!focusables.length) return;
            var first = focusables[0];
            var lastEl = focusables[focusables.length - 1];
            if (e.shiftKey && document.activeElement === first) { e.preventDefault(); lastEl.focus(); }
            else if (!e.shiftKey && document.activeElement === lastEl) { e.preventDefault(); first.focus(); }
        });
    }

    // ---------- Toasts ----------
    var toastRegion = null;
    function ensureToastRegion() {
        if (toastRegion) return toastRegion;
        toastRegion = document.createElement('div');
        toastRegion.className = 'toast-region';
        toastRegion.setAttribute('role', 'status');
        toastRegion.setAttribute('aria-live', 'polite');
        document.body.appendChild(toastRegion);
        return toastRegion;
    }

    window.showToast = function (message, type) {
        type = type || 'info';
        var region = ensureToastRegion();
        var toast = document.createElement('div');
        toast.className = 'app-toast ' + type;
        var icon = type === 'error' ? 'bi-exclamation-octagon' : (type === 'success' ? 'bi-check-circle' : 'bi-info-circle');
        toast.innerHTML = '<i class="bi ' + icon + '" aria-hidden="true"></i><span>' + message + '</span>';
        region.appendChild(toast);
        setTimeout(function () {
            toast.style.opacity = '0';
            toast.style.transform = 'translateY(8px)';
            setTimeout(function () { if (toast.parentNode) toast.parentNode.removeChild(toast); }, 220);
        }, 4200);
    };

    // ---------- fetch helpers ----------
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
        }).catch(function () {
            window.showToast('تعذر الاتصال بالخادم، حاول مرة أخرى', 'error');
            throw new Error('network');
        });
    }

    function handleResult(res, errorMsg) {
        if (res.ok) {
            location.reload();
        } else {
            res.text().then(function (text) { window.showToast(text || errorMsg, 'error'); });
        }
    }

    // ---------- Accessible edit-name modal (replaces prompt()) ----------
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
            '      <button type="submit" class="btn btn-primary"><i class="bi bi-check-lg" aria-hidden="true"></i> حفظ</button>' +
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
                        window.showToast('تم الحفظ بنجاح', 'success');
                        location.reload();
                    } else {
                        res.text().then(function (text) { window.showToast(text || 'تعذر تعديل العنصر', 'error'); });
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
                if (window.confirm('هل أنت متأكد من الحذف؟')) {
                    postForm('/' + endpoint + '/Delete?id=' + delBtn.dataset.id, {})
                        .then(function (res) { handleResult(res, 'لا يمكن حذف العنصر'); });
                }
            }
        });
    });

    // Strip unselected item rows before batch/quote submits
    document.addEventListener('submit', function (e) {
        var tbody = e.target.querySelector('#itemsBody');
        if (!tbody) return;
        tbody.querySelectorAll('tr').forEach(function (tr) {
            var sel = tr.querySelector('.item-select');
            if (sel && !sel.value) tr.remove();
        });
    });

    // Scrollable table regions are keyboard-focusable (axe: scrollable-region-focusable)
    document.querySelectorAll('.table-container').forEach(function (box) {
        if (box.scrollHeight > box.clientHeight || box.scrollWidth > box.clientWidth) {
            box.setAttribute('tabindex', '0');
            box.setAttribute('role', 'region');
            box.setAttribute('aria-label', 'جدول قابل للتمرير');
        }
    });
})();