// NewVixSmart.Web - ARIA tab pattern (roving tabindex + keyboard support)
(function () {
    'use strict';

    var MARKER = 'data-tablist-roving';
    var TABLIST_SELECTOR = '[role="tablist"]';
    var TAB_SELECTOR = '[role="tab"]';

    function toArray(nodeList) {
        return Array.prototype.slice.call(nodeList);
    }

    function tabs(tablist) {
        return toArray(tablist.querySelectorAll(TAB_SELECTOR));
    }

    function isSelected(tab) {
        return tab.getAttribute('aria-selected') === 'true';
    }

    // The server-rendered aria-selected is the source of truth; only fall back
    // to the first tab when no tab is marked as selected.
    function selectedIndex(list) {
        for (var i = 0; i < list.length; i++) {
            if (isSelected(list[i])) return i;
        }
        return list.length > 0 ? 0 : -1;
    }

    // One tab stop for the whole tablist: the selected tab keeps tabindex="0".
    function syncTabindex(list) {
        var active = selectedIndex(list);
        list.forEach(function (tab, index) {
            tab.setAttribute('tabindex', index === active ? '0' : '-1');
        });
    }

    function panelFor(tab) {
        var ids = (tab.getAttribute('aria-controls') || '').split(/\s+/);
        for (var i = 0; i < ids.length; i++) {
            if (!ids[i]) continue;
            var panel = document.getElementById(ids[i]);
            if (panel) return panel;
        }
        return null;
    }

    // Activate by dispatching a real click so a click handler the page already
    // registered on the tab runs exactly once (no second listener is added).
    // If nothing reacted to the click, apply the standard tab semantics.
    function activate(tablist, tab) {
        tab.click();
        if (isSelected(tab)) {
            syncTabindex(tabs(tablist));
            return;
        }
        var list = tabs(tablist);
        list.forEach(function (other) {
            other.setAttribute('aria-selected', other === tab ? 'true' : 'false');
            var panel = panelFor(other);
            if (!panel) return;
            if (other === tab) panel.removeAttribute('hidden');
            else panel.hidden = true;
        });
        syncTabindex(list);
    }

    function onKeydown(event) {
        if (event.altKey || event.ctrlKey || event.metaKey) return;

        var tablist = this;
        var tab = event.target && event.target.closest ? event.target.closest(TAB_SELECTOR) : null;
        if (!tab || !tablist.contains(tab)) return;

        var list = tabs(tablist);
        if (list.length === 0) return;
        var index = list.indexOf(tab);
        if (index < 0) return;

        var next = -1;
        switch (event.key) {
            case 'ArrowRight':
            case 'ArrowDown':
                next = (index + 1) % list.length;
                break;
            case 'ArrowLeft':
            case 'ArrowUp':
                next = (index - 1 + list.length) % list.length;
                break;
            case 'Home':
                next = 0;
                break;
            case 'End':
                next = list.length - 1;
                break;
            case 'Enter':
            case ' ':
            case 'Spacebar':
                // Prevents the native button activation so the tab is activated once.
                event.preventDefault();
                activate(tablist, tab);
                return;
            default:
                return;
        }

        event.preventDefault();
        list[next].focus();
        activate(tablist, list[next]);
    }

    // Idempotent: a tablist already carrying the marker keeps its single listener.
    function init(root) {
        var lists = toArray((root || document).querySelectorAll(TABLIST_SELECTOR));
        lists.forEach(function (tablist) {
            if (tablist.hasAttribute(MARKER)) return;
            var list = tabs(tablist);
            if (list.length === 0) return;
            tablist.setAttribute(MARKER, '');
            tablist.addEventListener('keydown', onKeydown);
            syncTabindex(list);
        });
    }

    window.initTabLists = init;

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(); });
    } else {
        init();
    }
})();
