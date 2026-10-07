// NewVixSmart.Web - site-wide scripts
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

    function syncSidebarInert() {
        if (!sidebar) return;
        if (isResponsive() && !sidebar.classList.contains('show')) {
            sidebar.setAttribute('inert', '');
        } else {
            sidebar.removeAttribute('inert');
        }
    }

    function setSidebarOpen(open) {
        if (!sidebar || !isResponsive()) {
            if (sidebar && !open) sidebar.classList.remove('show');
            if (overlay) overlay.classList.remove('show');
            syncSidebarInert();
            return;
        }
        sidebar.classList.toggle('show', open);
        overlay.classList.toggle('show', open);
        overlay.setAttribute('aria-hidden', open ? 'false' : 'true');
        if (toggle) toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        document.body.style.overflow = open ? 'hidden' : '';
        syncSidebarInert();
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
        overlay.addEventListener('click', function () {
            setSidebarOpen(false);
            if (lastFocus && lastFocus.focus) lastFocus.focus();
            else if (toggle) toggle.focus();
        });
        sidebar.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && sidebar.classList.contains('show')) {
                window.toggleSidebar();
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
            var focusables = Array.prototype.slice.call(
                sidebar.querySelectorAll('a[href], button:not([disabled]), input:not([disabled])')
            ).filter(function (el) { return !el.closest('.is-collapsed'); });
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

        var iconEl = document.createElement('i');
        iconEl.className = 'bi ' + (type === 'error' ? 'bi-exclamation-octagon' : (type === 'success' ? 'bi-check-circle' : 'bi-info-circle'));
        iconEl.setAttribute('aria-hidden', 'true');
        toast.appendChild(iconEl);

        var textEl = document.createElement('span');
        textEl.textContent = message;
        toast.appendChild(textEl);

        var progressEl = document.createElement('span');
        progressEl.className = 'toast-progress';
        progressEl.setAttribute('aria-hidden', 'true');
        toast.appendChild(progressEl);

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
        div.innerHTML = `
            <div class="modal-dialog">
              <form id="editNameForm" class="modal-content" novalidate>
                <div class="modal-header">
                  <h5 class="modal-title" id="editNameTitle">تعديل الاسم</h5>
                  <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="إغلاق"></button>
                </div>
                <div class="modal-body">
                  <label for="editNameInput" class="form-label">الاسم الجديد</label>
                  <input type="text" id="editNameInput" class="form-control" required maxlength="200" />
                </div>
                <div class="modal-footer">
                  <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">إلغاء</button>
                  <button type="submit" class="btn btn-primary"><i class="bi bi-check-lg" aria-hidden="true"></i> حفظ</button>
                </div>
              </form>
            </div>`;

        document.body.appendChild(div);

        div.querySelector('#editNameForm').addEventListener('submit', function (e) {
            e.preventDefault();
            var input = div.querySelector('#editNameInput');
            var val = input.value.trim();
            if (!val || !editTarget) return;
            var payload = { Id: editTarget.id, Name: val };
            if (editTarget.rowVersion) payload.RowVersion = editTarget.rowVersion;
            postForm('/' + editTarget.endpoint + '/Edit', payload)
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

        div.addEventListener('hidden.bs.modal', function () {
            var input = div.querySelector('#editNameInput');
            if (input) input.value = '';
        });

        editModal = new bootstrap.Modal(div, {});
    }

    // ---------- Accessible confirm modal (replaces window.confirm) ----------
    var confirmModal = null;
    var confirmModalEl = null;
    var confirmCallback = null;

    function ensureConfirmModal() {
        if (confirmModalEl) return confirmModalEl;
        var div = document.createElement('div');
        div.className = 'modal fade';
        div.id = 'appConfirmModal';
        div.setAttribute('tabindex', '-1');
        div.setAttribute('role', 'alertdialog');
        div.setAttribute('aria-modal', 'true');
        div.setAttribute('aria-hidden', 'true');
        div.setAttribute('aria-labelledby', 'appConfirmTitle');
        div.setAttribute('aria-describedby', 'appConfirmMsg');
        div.innerHTML = `
            <div class="modal-dialog modal-sm">
              <div class="modal-content">
                <div class="modal-header">
                  <h5 class="modal-title" id="appConfirmTitle">تأكيد العملية</h5>
                  <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="إغلاق"></button>
                </div>
                <div class="modal-body">
                  <p class="mb-0" id="appConfirmMsg"></p>
                </div>
                <div class="modal-footer">
                  <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">إلغاء</button>
                  <button type="button" class="btn btn-danger" id="appConfirmBtn"><i class="bi bi-trash" aria-hidden="true"></i> حذف</button>
                </div>
              </div>
            </div>`;

        document.body.appendChild(div);

        div.querySelector('#appConfirmBtn').addEventListener('click', function () {
            var fn = confirmCallback;
            confirmModal.hide();
            if (typeof fn === 'function') fn();
        });

        div.addEventListener('hidden.bs.modal', function () {
            confirmCallback = null;
        });

        confirmModalEl = div;
        confirmModal = new bootstrap.Modal(div, {});
        return confirmModalEl;
    }

    window.confirmAction = function (message, onConfirm, okLabel) {
        var div = ensureConfirmModal();
        div.querySelector('#appConfirmMsg').textContent = message;
        var btn = div.querySelector('#appConfirmBtn');
        var label = okLabel || 'تأكيد';
        var icon = /حذف/.test(label) ? 'bi-trash' : 'bi-check-lg';
        btn.innerHTML = '<i class="bi ' + icon + '" aria-hidden="true"></i> ' + label;
        confirmCallback = onConfirm;
        confirmModal.show();
        btn.focus();
    };

    var modalFocusSource = {};
    document.addEventListener('show.bs.modal', function (e) {
        var modal = e.target;
        if (!modal || !modal.id) return;
        if (document.activeElement && (document.activeElement === modal || modal.contains(document.activeElement))) return;
        modalFocusSource[modal.id] = document.activeElement;
    });
    document.addEventListener('hidden.bs.modal', function (e) {
        var modal = e.target;
        var source = modalFocusSource[modal.id];
        delete modalFocusSource[modal.id];
        if (source && source.focus) source.focus();
    });

    document.addEventListener('change', function (e) {
        var sel = e.target.closest('select[data-auto-submit]');
        if (!sel) return;
        var form = sel.closest('form');
        if (form) form.submit();
    });

    document.addEventListener('click', function (e) {
        var btn = e.target.closest('[data-auto-print]');
        if (btn) window.print();
    });

    function createCategory() {
        var input = document.getElementById('pageNewCat');
        if (!input) return;
        var name = input.value.trim();
        if (!name) {
            window.showToast('أدخل اسم التصنيف', 'error');
            input.focus();
            return;
        }
        postForm('/Categories/Create', { Name: name })
            .then(function (res) {
                if (res.ok) {
                    window.showToast('تم إضافة التصنيف بنجاح', 'success');
                    location.reload();
                } else {
                    res.text().then(function (text) { window.showToast(text || 'تعذر إضافة التصنيف', 'error'); });
                }
            });
    }

    document.addEventListener('click', function (e) {
        var focusBtn = e.target.closest('[data-focus-target]');
        if (focusBtn) {
            var focusId = focusBtn.getAttribute('data-focus-target');
            var focusEl = document.getElementById(focusId);
            if (focusEl) focusEl.focus();
        }
        if (e.target.closest('[data-create-category]')) createCategory();
    });

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && e.target && e.target.id === 'pageNewCat') {
            e.preventDefault();
            createCategory();
        }
    });

    // Manage categories & item types rows (edit / delete)
    function manageEndpointFor(table) {
        return table.id === 'categoriesTable' ? 'Categories' : 'ItemTypes';
    }

    function bindManageTable(table) {
        var endpoint = manageEndpointFor(table);

        table.addEventListener('click', function (e) {
            var editBtn = e.target.closest('.btn-edit');
            var delBtn = e.target.closest('.btn-del');

            if (editBtn) {
                var row = editBtn.closest('tr');
                var nameTd = row.querySelector('td[data-name]');
                var current = nameTd ? nameTd.dataset.name : '';
                ensureEditModal();
                // الرمز يُقرأ من الصفّ المعروض فيُحمل إلى الخادم ليتحقّق من أنّ الصفّ لم
                // يتغيّر بين العرض والحفظ؛ وإلا قام آخر كاتبٍ فوق هذا التعديل دون أن يعلم.
                editTarget = { id: editBtn.dataset.id, endpoint: endpoint, rowVersion: row.dataset.rowversion || '' };
                var input = document.getElementById('editNameInput');
                input.value = current;
                editModal.show();
                input.focus();
                input.select();
            }

            if (delBtn) {
                var id = delBtn.dataset.id;
                confirmAction('هل أنت متأكد من الحذف؟', function () {
                    postForm('/' + endpoint + '/Delete?id=' + id, {})
                        .then(function (res) { handleResult(res, 'لا يمكن حذف العنصر'); });
                }, 'حذف');
            }
        });
    }

    ['categoriesTable', 'itemTypesTable'].forEach(function (tableId) {
        var table = document.getElementById(tableId);
        if (table) bindManageTable(table);
    });

    // ---------- Skeleton loaders (data-load tables) ----------
    function renderSkeleton(table, rows) {
        var tbody = table.tBodies[0] || table.createTBody();
        var colCount = table.querySelectorAll('thead th').length || 3;
        var html = '';
        for (var r = 0; r < rows; r++) {
            html += '<tr class="skeleton-row">';
            for (var c = 0; c < colCount; c++) {
                html += '<td><span class="skeleton" style="width:' + (100 - c * 18) + '%"></span></td>';
            }
            html += '</tr>';
        }
        tbody.innerHTML = html;
    }

    function hydrateTable(table) {
        var endpoint = table.getAttribute('data-endpoint') || location.pathname + location.search;
        var original = table.innerHTML;
        renderSkeleton(table, 5);
        table.setAttribute('aria-busy', 'true');
        var started = Date.now();
        fetch(endpoint, { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
            .then(function (res) { return res.ok ? res.text() : Promise.reject(new Error('load failed')); })
            .then(function (html) {
                var wait = Math.max(0, 450 - (Date.now() - started));
                setTimeout(function () {
                    var parsed = document.createElement('template');
                    parsed.innerHTML = html;
                    var fresh = parsed.content.querySelector('table');
                    if (fresh) table.innerHTML = fresh.innerHTML;
                    table.setAttribute('aria-busy', 'false');
                    if (window.refreshVixTables) window.refreshVixTables();
                }, wait);
            })
            .catch(function () {
                table.innerHTML = original;
                table.setAttribute('aria-busy', 'false');
                window.showToast('تعذر تحميل البيانات', 'error');
            });
    }

    function initTables() {
        var targets = [];
        document.querySelectorAll('table[data-load="true"]').forEach(function (t) { targets.push(t); });
        ['categoriesTable', 'itemTypesTable'].forEach(function (tableId) {
            var table = document.getElementById(tableId);
            if (table && !table.hasAttribute('data-load')) targets.push(table);
        });
        targets.forEach(function (table) { hydrateTable(table); });
    }

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
    function scrollRegionLabel(el, index) {
        var label = '';
        var card = el.closest('.card');
        var hdr = card ? card.querySelector('.card-header') : null;
        if (hdr) label = hdr.textContent.replace(/\s+/g, ' ').trim();
        if (label.length > 40) label = label.slice(0, 40);
        return label ? label + ' — جدول قابل للتمرير (' + index + ')' : 'جدول قابل للتمرير (' + index + ')';
    }
    var scrollableRegions = 0;
    document.querySelectorAll('.table-container, .card-body.p-0, .table-responsive').forEach(function (box) {
        if (box.scrollHeight > box.clientHeight || box.scrollWidth > box.clientWidth) {
            box.setAttribute('tabindex', '0');
            box.setAttribute('role', 'region');
            box.setAttribute('aria-label', scrollRegionLabel(box, ++scrollableRegions));
        }
    });

    var sectionsKey = 'vix-sidebar-sections';
    var railKey = 'vix-sidebar-rail';
    var navGroups = document.querySelectorAll('.nav-group[data-group]');
    var railToggle = document.getElementById('railToggle');

    function setGroupState(group, expanded) {
        if (!group) return;
        group.classList.toggle('is-collapsed', !expanded);
        var btn = group.querySelector('.nav-group-header');
        if (btn) btn.setAttribute('aria-expanded', expanded ? 'true' : 'false');
    }

    function storeSections() {
        var state = {};
        navGroups.forEach(function (group) {
            var key = group.getAttribute('data-group');
            if (key) state[key] = !group.classList.contains('is-collapsed');
        });
        try { localStorage.setItem(sectionsKey, JSON.stringify(state)); } catch (e) { }
    }

    document.querySelectorAll('.nav-group-header').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var group = btn.closest('.nav-group');
            if (!group) return;
            setGroupState(group, group.classList.contains('is-collapsed'));
            if (sidebar && !sidebar.classList.contains('is-rail')) storeSections();
        });
    });

    function isDesktop() {
        return window.innerWidth >= 992;
    }

    var preRailState = null;

    function setRail(active) {
        if (!sidebar) return;
        if (!isDesktop()) {
            sidebar.classList.remove('is-rail');
            if (railToggle) railToggle.setAttribute('aria-expanded', 'true');
            return;
        }
        var wasRail = sidebar.classList.contains('is-rail');
        sidebar.classList.toggle('is-rail', active);
        if (railToggle) railToggle.setAttribute('aria-expanded', active ? 'false' : 'true');
        if (active && !wasRail) {
            preRailState = {};
            navGroups.forEach(function (group) {
                var key = group.getAttribute('data-group');
                preRailState[key] = !group.classList.contains('is-collapsed');
                setGroupState(group, false);
            });
        } else if (!active && wasRail) {
            if (preRailState) {
                navGroups.forEach(function (group) {
                    var key = group.getAttribute('data-group');
                    if (key && typeof preRailState[key] === 'boolean') setGroupState(group, preRailState[key]);
                });
            }
            preRailState = null;
        }
    }

    if (railToggle) {
        railToggle.addEventListener('click', function () {
            var on = sidebar ? !sidebar.classList.contains('is-rail') : false;
            setRail(on);
            try { localStorage.setItem(railKey, on ? '1' : '0'); } catch (e) { }
        });
    }

    window.addEventListener('resize', function () {
        if (sidebar && isDesktop() && sidebar.classList.contains('show')) sidebar.classList.remove('show');
        if (overlay && isDesktop() && overlay.classList.contains('show')) {
            overlay.classList.remove('show');
            overlay.setAttribute('aria-hidden', 'true');
        }
        if (!isResponsive()) document.body.style.overflow = '';
        var persisted = '0';
        try { persisted = localStorage.getItem(railKey) || '0'; } catch (e) { persisted = '0'; }
        setRail(persisted === '1');
        syncSidebarInert();
    });

    function initNavGroups() {
        var stored = null;
        try { stored = JSON.parse(localStorage.getItem(sectionsKey) || 'null'); } catch (e) { stored = null; }
        var activeGroup = null;
        var activeLink = sidebar ? sidebar.querySelector('.nav-link.active') : null;
        if (activeLink && activeLink.closest) activeGroup = activeLink.closest('.nav-group');
        navGroups.forEach(function (group) {
            var key = group.getAttribute('data-group');
            if (!key) return;
            var expanded = true;
            if (stored && typeof stored[key] === 'boolean') {
                expanded = stored[key];
            } else if (activeGroup) {
                expanded = (group === activeGroup);
            }
            if (activeGroup && group === activeGroup) expanded = true;
            setGroupState(group, expanded);
        });
    }

    function initRail() {
        var persisted = '0';
        try { persisted = localStorage.getItem(railKey) || '0'; } catch (e) { persisted = '0'; }
        setRail(persisted === '1');
    }

    function initSidebar() {
        initNavGroups();
        initRail();
        syncSidebarInert();
    }

    // ---------- Sidebar nav filter (type-to-search) ----------
    var navFilterInput = document.querySelector('[data-nav-filter]');
    var navFilterClear = document.querySelector('[data-nav-filter-clear]');
    var navFilterEmpty = document.querySelector('[data-nav-empty]');

    function clearNavFilter() {
        if (sidebar) sidebar.classList.remove('is-filtering');
        var all = document.querySelectorAll('.sidebar-nav .nav-link, .sidebar-nav .nav-group, .sidebar-nav .nav-section, .sidebar-nav .nav-subheader');
        for (var i = 0; i < all.length; i++) all[i].hidden = false;
        if (navFilterClear) navFilterClear.hidden = true;
        if (navFilterEmpty) navFilterEmpty.hidden = true;
    }

    function navFilterGroupText(link) {
        var group = link.closest ? link.closest('.nav-group') : null;
        var header = group ? group.querySelector('.nav-group-header') : null;
        return header ? (header.textContent || '') : '';
    }

    function navFilterHasLinksBeneath(el) {
        var node = el.nextElementSibling;
        while (node) {
            if (node.classList && (node.classList.contains('nav-section') || node.classList.contains('nav-subheader'))) break;
            if (node.classList && node.classList.contains('nav-link') && !node.hidden) return true;
            if (node.classList && node.classList.contains('nav-group')) {
                var groupLinks = node.querySelectorAll('.nav-link');
                for (var i = 0; i < groupLinks.length; i++) {
                    if (!groupLinks[i].hidden) return true;
                }
            }
            node = node.nextElementSibling;
        }
        return false;
    }

    function applyNavFilter() {
        if (!navFilterInput || !sidebar) {
            clearNavFilter();
            return;
        }
        var q = navFilterInput.value.trim().toLowerCase();
        if (!q) {
            clearNavFilter();
            return;
        }

        sidebar.classList.add('is-filtering');

        var links = document.querySelectorAll('.sidebar-nav .nav-link');
        var groups = document.querySelectorAll('.sidebar-nav .nav-group');
        var sections = document.querySelectorAll('.sidebar-nav .nav-section, .sidebar-nav .nav-subheader');

        var visible = 0;
        for (var i = 0; i < links.length; i++) {
            var link = links[i];
            var text = ((link.textContent || '') + ' ' + (link.getAttribute('aria-label') || '') + ' ' + navFilterGroupText(link)).toLowerCase();
            var match = text.indexOf(q) !== -1;
            link.hidden = !match;
            if (match) visible++;
        }

        for (var g = 0; g < groups.length; g++) {
            var group = groups[g];
            var groupLinks = group.querySelectorAll('.nav-link');
            var groupHasVisible = false;
            for (var gl = 0; gl < groupLinks.length; gl++) {
                if (!groupLinks[gl].hidden) { groupHasVisible = true; break; }
            }
            group.hidden = !groupHasVisible;
        }

        for (var s = 0; s < sections.length; s++) {
            sections[s].hidden = !navFilterHasLinksBeneath(sections[s]);
        }

        if (navFilterClear) navFilterClear.hidden = false;
        if (navFilterEmpty) navFilterEmpty.hidden = visible > 0;
    }

    function initNavFilter() {
        if (!sidebar || !navFilterInput) return;
        navFilterInput.addEventListener('input', applyNavFilter);
        if (navFilterClear) {
            navFilterClear.addEventListener('click', function () {
                navFilterInput.value = '';
                applyNavFilter();
            });
        }
        document.addEventListener('keydown', function (e) {
            var t = e.target;
            var editing = !!(t && t.closest && t.closest('input, textarea, select, [contenteditable]'));
            if ((e.ctrlKey || e.metaKey) && e.key && e.key.toLowerCase() === 'k') {
                if (editing) return;
                e.preventDefault();
                navFilterInput.focus();
                return;
            }
            if (e.key === 'Escape' && document.activeElement === navFilterInput) {
                navFilterInput.value = '';
                applyNavFilter();
            }
        });
    }

    // ---------- Delegated accessible confirms (data-confirm) ----------
    document.addEventListener('submit', function (e) {
        var form = e.target.closest ? e.target.closest('form[data-confirm]') : null;
        if (!form) return;
        if (form.getAttribute('data-confirmed') === 'true') {
            form.removeAttribute('data-confirmed');
            return;
        }
        e.preventDefault();
        var msg = form.getAttribute('data-confirm');
        var ok = form.getAttribute('data-confirm-ok') || 'تأكيد';
        confirmAction(msg, function () {
            form.setAttribute('data-confirmed', 'true');
            form.submit();
        }, ok);
    });

    // ---------- Loading submit buttons ----------
    document.querySelectorAll('form[data-loading-submit]').forEach(function (form) {
        form.addEventListener('submit', function () {
            var btn = form.querySelector('[type="submit"]');
            if (!btn || btn.disabled) return;
            btn.disabled = true;
            var loading = form.getAttribute('data-loading-text') || 'جارٍ التنفيذ...';
            btn.dataset.originalHtml = btn.innerHTML;
            btn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span> ' + loading;
        });
    });

    // ---------- Everyday greeting ----------
    var greetingEl = document.querySelector('[data-greeting]');
    if (greetingEl) {
        var oh = new Date().getHours();
        var greeting = oh >= 5 && oh < 12 ? 'صباح الخير' : (oh >= 12 && oh < 18 ? 'مساء الخير' : 'تصبحون على خير');
        greetingEl.textContent = greeting;
    }

    // ---------- Password visibility toggle ----------
    document.querySelectorAll('[data-password-toggle]').forEach(function (btn) {
        btn.addEventListener('click', function () {
            var target = document.getElementById(btn.getAttribute('aria-controls'));
            if (!target) return;
            var show = target.type === 'password';
            target.type = show ? 'text' : 'password';
            btn.setAttribute('aria-pressed', show ? 'true' : 'false');
            var icon = btn.querySelector('i');
            if (icon) icon.className = 'bi ' + (show ? 'bi-eye-slash' : 'bi-eye');
            btn.setAttribute('aria-label', show ? 'إخفاء كلمة المرور' : 'إظهار كلمة المرور');
            target.focus();
        });
    });

    // ---------- Count-up stats ----------
    function prefersReducedMotion() {
        return window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    function initCounters() {
        var els = document.querySelectorAll('[data-count]');
        if (!els.length) return;
        if (!('IntersectionObserver' in window)) {
            els.forEach(function (el) {
                var final = el.getAttribute('data-count-formatted');
                if (final !== null) el.textContent = final;
            });
            return;
        }
        els.forEach(function (el) {
            var target = parseFloat(el.getAttribute('data-count')) || 0;
            var decimals = parseInt(el.getAttribute('data-decimals') || '0', 10);
            var formatted = el.getAttribute('data-count-formatted');
            var obs = new IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    if (!entry.isIntersecting) return;
                    obs.unobserve(el);
                    if (prefersReducedMotion()) {
                        if (formatted !== null) el.textContent = formatted;
                        return;
                    }
                    var start = null;
                    function tick(ts) {
                        if (start === null) start = ts;
                        var p = Math.min((ts - start) / 1000, 1);
                        var eased = 1 - Math.pow(1 - p, 3);
                        var val = target * eased;
                        el.textContent = val.toLocaleString('en-US', { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
                        if (p < 1) {
                            requestAnimationFrame(tick);
                        } else if (formatted !== null) {
                            el.textContent = formatted;
                        }
                    }
                    requestAnimationFrame(tick);
                });
            }, { threshold: 0.4 });
            obs.observe(el);
        });
    }

    // Bulk-select (MassConvert) — select-all toggles all row checkboxes
    var selectAll = document.getElementById('selectAll');
    if (selectAll) {
        selectAll.addEventListener('change', function () {
            var checked = selectAll.checked;
            document.querySelectorAll('.mass-convert-check').forEach(function (cb) { cb.checked = checked; });
        });
    }

    function getStoredThemeMode() {
        var mode = 'system';
        try { mode = localStorage.getItem('theme-mode') || 'system'; } catch (e) { mode = 'system'; }
        if (mode !== 'system' && mode !== 'light' && mode !== 'dark') mode = 'system';
        return mode;
    }

    function setStoredThemeMode(mode) {
        try { localStorage.setItem('theme-mode', mode); } catch (e) { }
    }

    function applyThemeMode(mode) {
        var resolved = mode === 'system'
            ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
            : mode;
        document.documentElement.setAttribute('data-theme-mode', mode);
        document.documentElement.setAttribute('data-theme', resolved);
        document.documentElement.setAttribute('data-bs-theme', resolved);
        document.querySelectorAll('.theme-mode-btn').forEach(function (btn) {
            btn.setAttribute('aria-pressed', btn.getAttribute('data-theme-value') === mode ? 'true' : 'false');
        });
        if (window.__applyDarkThemeCss) { window.__applyDarkThemeCss(resolved === 'dark'); }
    }

    function initTheme() {
        var buttons = document.querySelectorAll('.theme-mode-btn');
        if (!buttons.length) return;
        buttons.forEach(function (btn) {
            btn.addEventListener('click', function () {
                var mode = btn.getAttribute('data-theme-value');
                if (mode !== 'system' && mode !== 'light' && mode !== 'dark') return;
                setStoredThemeMode(mode);
                applyThemeMode(mode);
            });
        });
        var mq = window.matchMedia('(prefers-color-scheme: dark)');
        function onSystemChange() {
            if (getStoredThemeMode() === 'system') applyThemeMode('system');
        }
        if (mq.addEventListener) mq.addEventListener('change', onSystemChange);
        else if (mq.addListener) mq.addListener(onSystemChange);
        applyThemeMode(getStoredThemeMode());
    }

    function initClock() {
        document.querySelectorAll('[data-clock]').forEach(function (el) {
            try {
                el.textContent = new Date().toLocaleDateString('ar', { weekday: 'long', day: 'numeric', month: 'long' });
            } catch (e) {
                el.textContent = '';
            }
        });
    }

    // ---------- Client-side data tables: full-table search / sort / pagination ----------
    var vixTables = [];

    function normalizeSearchText(text) {
        return String(text == null ? '' : text)
            .replace(/[\u064B-\u0652\u0640]/g, '')
            .replace(/[\u0623\u0625\u0622]/g, '\u0627')
            .replace(/\u0629/g, '\u0647')
            .replace(/\u0649/g, '\u064A')
            .toLowerCase();
    }

    function numericCellValue(text) {
        var t = String(text == null ? '' : text).replace(/[^\d\-.,]/g, '').replace(/,/g, '');
        if (t === '' || t === '-' || t === '.' || t === '-.') return null;
        var n = parseFloat(t);
        return isNaN(n) ? null : n;
    }

    function tableIsEditable(table) {
        if (table.id === 'itemsTable') return true;
        if (table.hasAttribute('data-table-plain')) return true;
        var tbody = table.tBodies && table.tBodies[0];
        var isLoad = table.hasAttribute('data-load') || table.id === 'categoriesTable' || table.id === 'itemTypesTable';
        if (!tbody || (!tbody.rows.length && !isLoad)) return true;
        if (tbody.querySelector('input:not([type="hidden"]), select, textarea')) return true;
        var head = table.tHead;
        if (!head || head.rows.length !== 1) return true;
        if (head.querySelector('th[colspan], th[rowspan]')) return true;
        return false;
    }

    function buildDataTable(table, idx) {
        if (table.closest('.data-shell') || tableIsEditable(table)) return;
        var container = table.closest('.table-container');
        if (!container) {
            container = document.createElement('div');
            container.className = 'table-container';
            table.parentNode.insertBefore(container, table);
            container.appendChild(table);
        }

        var shell = document.createElement('div');
        shell.className = 'data-shell';

        var toolbar = document.createElement('div');
        toolbar.className = 'data-toolbar';

        var searchWrap = document.createElement('div');
        searchWrap.className = 'data-search';
        var searchLabel = document.createElement('label');
        searchLabel.className = 'visually-hidden';
        searchLabel.setAttribute('for', 'dataSearch' + idx);
        searchLabel.textContent = 'بحث في الجدول';
        var searchIcon = document.createElement('i');
        searchIcon.className = 'bi bi-search';
        searchIcon.setAttribute('aria-hidden', 'true');
        var searchInput = document.createElement('input');
        searchInput.type = 'search';
        searchInput.id = 'dataSearch' + idx;
        searchInput.className = 'form-control form-control-sm';
        searchInput.setAttribute('placeholder', 'بحث في كامل الجدول...');
        searchInput.setAttribute('autocomplete', 'off');
        searchWrap.appendChild(searchLabel);
        searchWrap.appendChild(searchIcon);
        searchWrap.appendChild(searchInput);

        var sizeWrap = document.createElement('div');
        sizeWrap.className = 'data-size';
        var sizeLabel = document.createElement('label');
        sizeLabel.className = 'visually-hidden';
        sizeLabel.setAttribute('for', 'dataSize' + idx);
        sizeLabel.textContent = 'عدد الصفوف في الصفحة';
        var sizeSelect = document.createElement('select');
        sizeSelect.id = 'dataSize' + idx;
        sizeSelect.className = 'form-select form-select-sm';
        [10, 20, 50, 100].forEach(function (n) {
            var opt = document.createElement('option');
            opt.value = n;
            opt.textContent = n;
            sizeSelect.appendChild(opt);
        });
        var savedSize = 20;
        try { savedSize = parseInt(localStorage.getItem('vix-data-size') || '20', 10); } catch (e) { savedSize = 20; }
        if ([10, 20, 50, 100].indexOf(savedSize) === -1) savedSize = 20;
        sizeSelect.value = String(savedSize);
        sizeWrap.appendChild(sizeLabel);
        sizeWrap.appendChild(sizeSelect);

        var count = document.createElement('div');
        count.className = 'data-count';
        count.setAttribute('role', 'status');
        count.setAttribute('aria-live', 'polite');

        toolbar.appendChild(searchWrap);
        toolbar.appendChild(count);
        toolbar.appendChild(sizeWrap);

        var nav = document.createElement('nav');
        nav.className = 'data-pager';
        nav.setAttribute('aria-label', 'ترقيم صفحات الجدول');

        container.parentNode.insertBefore(shell, container);
        shell.appendChild(toolbar);
        shell.appendChild(container);
        shell.appendChild(nav);

        var state = {
            table: table,
            tbody: table.tBodies[0],
            nav: nav,
            count: count,
            searchInput: searchInput,
            sizeSelect: sizeSelect,
            page: 1,
            size: savedSize,
            sortIdx: -1,
            sortDir: 1,
            pagerNavigated: false
        };
        vixTables.push(state);

        searchInput.addEventListener('input', function () {
            state.page = 1;
            renderDataTable(state);
        });
        sizeSelect.addEventListener('change', function () {
            state.size = parseInt(sizeSelect.value, 10) || 20;
            try { localStorage.setItem('vix-data-size', String(state.size)); } catch (e) { }
            state.page = 1;
            renderDataTable(state);
        });

        makeHeadersSortable(state);
        renderDataTable(state);
    }

    function makeHeadersSortable(state) {
        var head = state.table.tHead;
        if (!head || !head.rows.length) return;
        var cells = head.rows[0].cells;
        Array.prototype.forEach.call(cells, function (th, col) {
            if (!(th.textContent || '').trim()) return;
            if (th.classList.contains('visually-hidden')) return;
            th.setAttribute('aria-sort', 'none');
            var btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'th-sort';
            while (th.firstChild) btn.appendChild(th.firstChild);
            var icon = document.createElement('i');
            icon.className = 'bi bi-arrow-down-up';
            icon.setAttribute('aria-hidden', 'true');
            btn.appendChild(icon);
            th.appendChild(btn);
            btn.addEventListener('click', function () {
                if (state.sortIdx === col) {
                    state.sortDir = state.sortDir === 1 ? -1 : 1;
                } else {
                    state.sortIdx = col;
                    state.sortDir = 1;
                }
                applySortIndicator(state, col);
                state.page = 1;
                renderDataTable(state);
            });
        });
    }

    function applySortIndicator(state, col) {
        var head = state.table.tHead;
        if (!head || !head.rows.length) return;
        Array.prototype.forEach.call(head.rows[0].cells, function (th, c) {
            var btn = th.querySelector('.th-sort .bi');
            if (!btn) return;
            btn.className = 'bi ' + (c === state.sortIdx
                ? (state.sortDir === 1 ? 'bi-arrow-up' : 'bi-arrow-down')
                : 'bi-arrow-down-up');
            th.setAttribute('aria-sort', c === state.sortIdx ? (state.sortDir === 1 ? 'ascending' : 'descending') : 'none');
        });
    }

    function pageItem(state, ul, label, target, opts) {
        opts = opts || {};
        var li = document.createElement('li');
        li.className = 'page-item' + (opts.disabled ? ' disabled' : '') + (opts.current ? ' active' : '');
        if (opts.disabled) li.setAttribute('aria-disabled', 'true');
        var btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'page-link';
        btn.textContent = label;
        btn.setAttribute('aria-label', opts.ariaLabel || ('صفحة ' + label));
        if (opts.current) btn.setAttribute('aria-current', 'page');
        if (!opts.disabled) {
            (function (targetPage) {
                btn.addEventListener('click', function () {
                    state.page = targetPage;
                    state.pagerNavigated = true;
                    renderDataTable(state);
                });
            })(target);
        }
        li.appendChild(btn);
        ul.appendChild(li);
    }

    function renderPager(state, page, pages) {
        state.nav.innerHTML = '';
        if (pages <= 1) return;
        var ul = document.createElement('ul');
        ul.className = 'pagination pagination-sm mb-0';
        pageItem(state, ul, '»', page > 1 ? page - 1 : null, { ariaLabel: 'السابق', disabled: page <= 1 });
        var win = 2;
        var startN = Math.max(1, page - win);
        var endN = Math.min(pages, page + win);
        if (startN > 1) {
            pageItem(state, ul, '1', 1, { current: page === 1 });
            if (startN > 2) pageItem(state, ul, '…', null, { disabled: true, ariaLabel: 'صفحات أخرى' });
        }
        for (var n = startN; n <= endN; n++) pageItem(state, ul, String(n), n, { current: n === page });
        if (endN < pages) {
            if (endN < pages - 1) pageItem(state, ul, '…', null, { disabled: true, ariaLabel: 'صفحات أخرى' });
            pageItem(state, ul, String(pages), pages, { current: page === pages });
        }
        pageItem(state, ul, '«', page < pages ? page + 1 : null, { ariaLabel: 'التالي', disabled: page >= pages });
        state.nav.appendChild(ul);
    }

    function setRowHidden(tr, hidden) {
        tr.classList.toggle('data-hidden', hidden);
        if (hidden) {
            tr.setAttribute('inert', '');
            tr.setAttribute('aria-hidden', 'true');
        } else {
            tr.removeAttribute('inert');
            tr.removeAttribute('aria-hidden');
        }
    }

    function renderDataTable(state) {
        var rows = Array.prototype.filter.call(state.tbody.rows, function (tr) {
            return !tr.classList.contains('data-message');
        });
        var search = normalizeSearchText(state.searchInput ? state.searchInput.value : '');
        rows.forEach(function (tr) {
            var match = !search;
            if (!match) {
                var text = '';
                Array.prototype.forEach.call(tr.cells, function (td) { text += ' ' + (td.textContent || ''); });
                match = normalizeSearchText(text).indexOf(search) !== -1;
            }
            setRowHidden(tr, !match);
        });
        var visible = rows.filter(function (tr) { return !tr.classList.contains('data-hidden'); });

        if (state.sortIdx >= 0 && visible.length && state.sortIdx < visible[0].cells.length) {
            visible.sort(function (a, b) {
                var at = a.cells[state.sortIdx] ? (a.cells[state.sortIdx].textContent || '') : '';
                var bt = b.cells[state.sortIdx] ? (b.cells[state.sortIdx].textContent || '') : '';
                var an = numericCellValue(at);
                var bn = numericCellValue(bt);
                var cmp = (an !== null && bn !== null) ? an - bn : normalizeSearchText(at).localeCompare(normalizeSearchText(bt), 'ar');
                return cmp * state.sortDir;
            });
            visible.forEach(function (tr) { state.tbody.appendChild(tr); });
        }

        var total = visible.length;
        var pages = Math.max(1, Math.ceil(total / state.size));
        if (state.page > pages) state.page = pages;
        var start = (state.page - 1) * state.size;
        var end = Math.min(start + state.size, total);
        visible.forEach(function (tr, i) { setRowHidden(tr, i < start || i >= end); });

        Array.prototype.forEach.call(state.tbody.querySelectorAll('tr.data-message'), function (tr) { tr.remove(); });
        if (total === 0 && rows.length > 0) {
            var colCount = 0;
            if (state.table.tHead && state.table.tHead.rows[0]) colCount = state.table.tHead.rows[0].cells.length;
            var msg = document.createElement('tr');
            msg.className = 'data-message';
            var td = document.createElement('td');
            td.colSpan = Math.max(colCount, 1);
            td.className = 'text-center text-muted py-4';
            td.textContent = 'لا توجد نتائج مطابقة';
            msg.appendChild(td);
            state.tbody.appendChild(msg);
        }
        state.count.textContent = total === 0 ? '0 نتيجة'
            : 'صفحة ' + state.page + ' — عرض ' + (start + 1) + '–' + end + ' من ' + total;
        renderPager(state, state.page, pages);

        if (state.pagerNavigated) {
            state.pagerNavigated = false;
            if (state.searchInput && typeof state.searchInput.focus === 'function') state.searchInput.focus();
        }
    }

    window.refreshVixTables = function () {
        vixTables.forEach(function (state) {
            state.tbody = state.table.tBodies[0];
            if (state.table.tHead && !state.table.tHead.querySelector('.th-sort')) {
                makeHeadersSortable(state);
                applySortIndicator(state, state.sortIdx);
            }
            renderDataTable(state);
        });
    };

    function initDataTables() {
        var seen = [];
        var tables = document.querySelectorAll('.table-container .table, table[data-load="true"], table#categoriesTable, table#itemTypesTable');
        tables.forEach(function (table) {
            if (seen.indexOf(table) !== -1) return;
            seen.push(table);
            buildDataTable(table, seen.length - 1);
        });
        applyFirstColFreezeAll();
    }

    function applyFirstColFreezeAll() {
        document.querySelectorAll('.table-container .table').forEach(function (table) {
            var head = table.tHead;
            if (!head || !head.rows.length || table.querySelector('.nvs-frozen-col')) return;
            var colCount = head.rows[0].cells.length;
            if (colCount <= 8) return;
            head.rows[0].cells[0].classList.add('nvs-frozen-col');
            [].forEach.call(table.rows, function (tr) {
                var cell0 = tr.cells && tr.cells[0];
                if (cell0 && tr.cells.length === colCount) cell0.classList.add('nvs-frozen-col');
            });
        });
    }

    function applyTableDensity(density) {
        document.body.setAttribute('data-table-density', density);
        var btn = document.querySelector('[data-table-density-toggle]');
        if (!btn) return;
        var pressed = density === 'comfortable';
        btn.setAttribute('aria-pressed', pressed ? 'true' : 'false');
        var label = btn.querySelector('.theme-mode-label');
        if (label) label.textContent = pressed ? 'كثافة مريحة' : 'كثافة مضغوطة';
        var icon = btn.querySelector('.bi');
        if (icon) icon.className = 'bi ' + (pressed ? 'bi-arrows-angle-expand' : 'bi-arrows-collapse');
    }

    function initTableDensity() {
        var stored = 'compact';
        try { stored = localStorage.getItem('vix-table-density') || 'compact'; } catch (e) { stored = 'compact'; }
        applyTableDensity(stored === 'comfortable' ? 'comfortable' : 'compact');
        document.addEventListener('click', function (e) {
            var btn = e.target && e.target.closest ? e.target.closest('[data-table-density-toggle]') : null;
            if (!btn) return;
            var next = document.body.getAttribute('data-table-density') === 'comfortable' ? 'compact' : 'comfortable';
            try { localStorage.setItem('vix-table-density', next); } catch (err) { }
            applyTableDensity(next);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { initSidebar(); initNavFilter(); initTables(); initCounters(); initTheme(); initClock(); initDataTables(); initTableDensity(); });
    } else {
        initSidebar();
        initNavFilter();
        initTables();
        initCounters();
        initTheme();
        initClock();
        initDataTables();
        initTableDensity();
    }

    // ---------- Prefetch same-origin navigation on hover/focus ----------
    var prefetched = new Set();

    function canPrefetch(a) {
        if (!a || !a.href) return false;
        var href = a.getAttribute('href');
        if (!href) return false;
        if (href.charAt(0) === '#') return false;
        if (/^(mailto:|tel:|javascript:)/i.test(href)) return false;
        if (a.target === '_blank') return false;
        if (a.hasAttribute('download')) return false;
        if (location.protocol === 'file:') return false;
        try {
            if (new URL(a.href, location.href).origin !== location.origin) return false;
        } catch (err) { return false; }
        if (navigator.connection) {
            if (navigator.connection.saveData === true) return false;
            var etype = navigator.connection.effectiveType || '';
            if (etype.indexOf('2g') !== -1) return false;
        }
        return true;
    }

    function prefetchDocument(linkEl) {
        var absUrl = linkEl.href;
        if (prefetched.has(absUrl)) return;
        prefetched.add(absUrl);
        if (prefetched.size > 64) {
            var oldest = prefetched.values().next();
            if (!oldest.done) prefetched.delete(oldest.value);
        }
        try {
            var p = document.createElement('link');
            p.rel = 'prefetch';
            p.as = 'document';
            p.href = absUrl;
            document.head.appendChild(p);
        } catch (err) { }
    }

    document.addEventListener('mouseover', function (e) {
        var a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
        if (a && canPrefetch(a)) prefetchDocument(a);
    }, true);

    document.addEventListener('focusin', function (e) {
        var a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
        if (a && canPrefetch(a)) prefetchDocument(a);
    }, true);
})();