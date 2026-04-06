// wwwroot/js/tasktracker.js
var TaskTracker = (function() {
    'use strict';

    // ========== UTILITIES ==========
    function escapeHtml(text) {
        if (!text) return text;
        return text.replace(/&/g, '&amp;')
                   .replace(/</g, '&lt;')
                   .replace(/>/g, '&gt;')
                   .replace(/"/g, '&quot;')
                   .replace(/'/g, '&#39;');
    }

    function getInitials(name) {
        if (!name) return '?';
        var parts = name.split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.length === 0) return '?';
        if (parts.length === 1) return parts[0].substring(0, 1).toUpperCase();
        return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase();
    }

    function formatDate(date, format) {
        var d = new Date(date);
        if (isNaN(d.getTime())) return '';
        if (format === 'short') return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
        if (format === 'medium') return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' });
        return d.toLocaleDateString();
    }

    function formatTime12(date) {
        var d = new Date(date);
        if (isNaN(d.getTime())) return '--:--';
        var hours = d.getHours();
        var minutes = d.getMinutes();
        var ampm = hours >= 12 ? 'PM' : 'AM';
        hours = hours % 12 || 12;
        return hours + ':' + String(minutes).padStart(2, '0') + ' ' + ampm;
    }

    // ========== NOTIFICATION ==========
    function showNotification(message, type) {
        var colors = {
            success: 'bg-green-500',
            error: 'bg-red-500',
            info: 'bg-blue-500',
            warning: 'bg-yellow-500'
        };
        var icons = {
            success: 'fa-check-circle',
            error: 'fa-exclamation-circle',
            info: 'fa-info-circle',
            warning: 'fa-exclamation-triangle'
        };
        
        $('.tasktracker-notification').remove();
        var notification = $('<div class="tasktracker-notification fixed top-4 right-4 z-50 px-4 py-2 rounded-lg shadow-lg text-sm font-medium text-white ' + colors[type] + ' animate-slide-in flex items-center">' +
            '<i class="fas ' + icons[type] + ' mr-2"></i>' +
            '<span>' + escapeHtml(message) + '</span>' +
            '</div>');
        $('body').append(notification);
        setTimeout(function() { notification.fadeOut(300, function() { $(this).remove(); }); }, 3000);
    }

    // ========== LOADING STATES ==========
    function showLoading(button, text) {
        var $btn = $(button);
        if ($btn.length) {
            $btn.data('original-text', $btn.html());
            $btn.html('<i class="fas fa-spinner fa-spin mr-2"></i>' + (text || 'Loading...'));
            $btn.prop('disabled', true).addClass('opacity-50 cursor-not-allowed');
        } else {
            $('#tasktracker-loading').remove();
            $('body').append('<div id="tasktracker-loading" class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50"><div class="bg-white rounded-lg p-4 flex items-center space-x-3"><div class="loading-spinner"></div><span>' + (text || 'Loading...') + '</span></div></div>');
        }
    }

    function hideLoading(button) {
        var $btn = $(button);
        if ($btn.length && $btn.data('original-text')) {
            $btn.html($btn.data('original-text'));
            $btn.prop('disabled', false).removeClass('opacity-50 cursor-not-allowed');
            $btn.removeData('original-text');
        } else {
            $('#tasktracker-loading').remove();
        }
    }

    // ========== MODAL MANAGEMENT ==========
    var Modal = {
        currentModal: null,
        
        open: function(modalId, options) {
            this.close();
            var $modal = $('#' + modalId);
            if ($modal.length) {
                $modal.removeClass('hidden').addClass('flex');
                this.currentModal = modalId;
                if (options && options.onOpen) options.onOpen($modal);
            }
        },
        
        close: function() {
            if (this.currentModal) {
                $('#' + this.currentModal).removeClass('flex').addClass('hidden');
                this.currentModal = null;
            }
        },
        
        closeOnEscape: function(e) {
            if (e.key === 'Escape') Modal.close();
        },
        
        closeOnOutsideClick: function(e) {
            if ($(e.target).hasClass('fixed')) Modal.close();
        }
    };

    // ========== API SERVICE ==========
    function ApiService(token) {
        this.token = token;
    }

    ApiService.prototype.request = function(url, method, data) {
        return new Promise(function(resolve, reject) {
            var options = {
                url: url,
                type: method,
                headers: { 'RequestVerificationToken': this.token },
                success: resolve,
                error: function(xhr) { reject({ error: xhr.statusText, response: xhr.responseText }); }
            };
            if (data) options.data = data;
            $.ajax(options);
        }.bind(this));
    };

    // ========== TABLE FILTERING ==========
    function setupTableFilter(tableId, searchInputId, filterSelectors) {
        function filter() {
            var searchTerm = $('#' + searchInputId).val().toLowerCase();
            var filters = {};
            for (var selector in filterSelectors) {
                filters[selector] = $(filterSelectors[selector]).val();
            }
            
            $(tableId + ' tbody tr').each(function() {
                var $row = $(this);
                var show = true;
                
                if (searchTerm) {
                    var text = $row.text().toLowerCase();
                    if (text.indexOf(searchTerm) === -1) show = false;
                }
                
                for (var key in filters) {
                    var filterValue = filters[key];
                    if (filterValue) {
                        var cellValue = $row.data(key) || '';
                        if (cellValue !== filterValue) show = false;
                    }
                }
                
                $row.toggle(show);
            });
        }
        
        $('#' + searchInputId).on('keyup', filter);
        for (var selector in filterSelectors) {
            $(filterSelectors[selector]).on('change', filter);
        }
        
        return { filter: filter };
    }

    // ========== COLOR HELPERS ==========
    function getScoreColor(score) {
        if (score >= 90) return { text: 'text-green-600', bg: 'bg-green-100', border: 'border-green-500', bar: '#22c55e' };
        if (score >= 75) return { text: 'text-blue-600', bg: 'bg-blue-100', border: 'border-blue-500', bar: '#3b82f6' };
        if (score >= 60) return { text: 'text-yellow-600', bg: 'bg-yellow-100', border: 'border-yellow-500', bar: '#eab308' };
        return { text: 'text-red-600', bg: 'bg-red-100', border: 'border-red-500', bar: '#ef4444' };
    }

    function getDelayColor(delayType) {
        switch(delayType) {
            case 'early': return { bg: 'bg-blue-100', text: 'text-blue-700', label: 'Early' };
            case 'on-time': return { bg: 'bg-green-100', text: 'text-green-700', label: 'On Time' };
            case 'minutes': return { bg: 'bg-yellow-100', text: 'text-yellow-700', label: 'Minutes Late' };
            case 'hours': return { bg: 'bg-orange-100', text: 'text-orange-700', label: 'Hours Late' };
            case 'days': return { bg: 'bg-red-100', text: 'text-red-700', label: 'Days Late' };
            default: return { bg: 'bg-gray-100', text: 'text-gray-600', label: 'Pending' };
        }
    }

    // ========== EXPORT PUBLIC API ==========
    return {
        // Utilities
        escapeHtml: escapeHtml,
        getInitials: getInitials,
        formatDate: formatDate,
        formatTime12: formatTime12,
        
        // UI Helpers
        showNotification: showNotification,
        showLoading: showLoading,
        hideLoading: hideLoading,
        
        // Modal
        Modal: Modal,
        
        // API
        ApiService: ApiService,
        
        // Table Filtering
        setupTableFilter: setupTableFilter,
        
        // Color Helpers
        getScoreColor: getScoreColor,
        getDelayColor: getDelayColor
    };
})();

// Auto-initialize modal close handlers
$(document).ready(function() {
    $(document).keydown(TaskTracker.Modal.closeOnEscape);
    $(window).click(TaskTracker.Modal.closeOnOutsideClick);
});