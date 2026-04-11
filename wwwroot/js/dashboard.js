// ========== CONSTANTS ==========
const DashboardConstants = {
    NOTIFICATION_DURATION_MS: 3000,
    SKELETON_FADE_MS: 300,
    BATCH_API_CHUNK_SIZE: 50,
    GRACE_PERIOD_MINUTES: 5,
    API_RETRY_ATTEMPTS: 3,
    API_RETRY_DELAY_MS: 1000
};

// ========== STATE MANAGEMENT ==========
const DashboardState = {
    token: null,
    currentDate: null,
    isHoliday: false,
    holidayName: '',
    previousDate: null,
    branchNotes: {},
    previousStats: { completed: 0, pending: 0, completionRate: 0 },
    pendingApiCalls: new Map(),
    abortControllers: new Map()
};

// ========== UTILITY FUNCTIONS ==========
function escapeHtml(str) {
    if (!str) return '';
    return str
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

function formatTime12(utcDateString) {
    if (!utcDateString) return "--:--";
    const utcDate = new Date(utcDateString);
    if (isNaN(utcDate.getTime())) return "--:--";
    let hours = utcDate.getHours();
    const minutes = utcDate.getMinutes();
    const ampm = hours >= 12 ? 'PM' : 'AM';
    hours = hours % 12 || 12;
    return `${hours}:${String(minutes).padStart(2, '0')} ${ampm}`;
}

function showNotification(message, type) {
    const colors = { 
        success: 'bg-green-500', 
        error: 'bg-red-500', 
        info: 'bg-blue-500', 
        warning: 'bg-yellow-500' 
    };
    
    $('.custom-notification').remove();
    const notification = $(`
        <div class="custom-notification fixed top-4 right-4 z-50 px-3 py-1.5 rounded shadow-lg text-xs text-white ${colors[type]} animate-slide-in">
            ${escapeHtml(message)}
        </div>
    `);
    $('body').append(notification);
    
    setTimeout(() => {
        notification.fadeOut(300, function() { $(this).remove(); });
    }, DashboardConstants.NOTIFICATION_DURATION_MS);
}

// ========== API SERVICE WITH RETRY AND ABORT ==========
class ApiService {
    constructor() {
        this.token = null;
    }

    setToken(token) {
        this.token = token;
    }

    async request(url, method, data, signal = null) {
        let lastError;
        
        for (let attempt = 1; attempt <= DashboardConstants.API_RETRY_ATTEMPTS; attempt++) {
            try {
                const options = {
                    url: url,
                    type: method,
                    headers: { 'RequestVerificationToken': this.token },
                    timeout: 30000
                };
                
                if (data) options.data = data;
                if (signal) options.xhr = () => {
                    const xhr = new XMLHttpRequest();
                    signal.addEventListener('abort', () => xhr.abort());
                    return xhr;
                };
                
                const response = await $.ajax(options);
                return response;
            } catch (error) {
                lastError = error;
                if (attempt < DashboardConstants.API_RETRY_ATTEMPTS) {
                    await new Promise(resolve => setTimeout(resolve, DashboardConstants.API_RETRY_DELAY_MS * attempt));
                }
            }
        }
        
        throw lastError;
    }

    async getTaskStatusesBatch(branchIds, taskIds, date) {
        const key = `${date}_${branchIds.join(',')}_${taskIds.join(',')}`;
        
        if (DashboardState.pendingApiCalls.has(key)) {
            return DashboardState.pendingApiCalls.get(key);
        }
        
        const promise = this.request('/Home/GetAllTaskStatuses', 'GET', {
            date: date,
            branchIds: branchIds.join(','),
            taskIds: taskIds.join(',')
        });
        
        DashboardState.pendingApiCalls.set(key, promise);
        
        try {
            const result = await promise;
            return result;
        } finally {
            DashboardState.pendingApiCalls.delete(key);
        }
    }

    async updateTaskTime(branchId, taskItemId, date, completionTime) {
        return this.request('/Home/UpdateTaskTime', 'POST', {
            branchId: branchId,
            taskItemId: taskItemId,
            date: date,
            completionTime: completionTime
        });
    }

    async resetTask(branchId, taskItemId, date) {
        return this.request('/Home/ResetTask', 'POST', {
            branchId: branchId,
            taskItemId: taskItemId,
            date: date
        });
    }

    async bulkUpdate(taskItemId, completionDateTime, branchIds) {
        return this.request('/Home/BulkUpdate', 'POST', {
            taskItemId: taskItemId,
            completionDateTime: completionDateTime,
            branchIds: branchIds
        });
    }

    async resetAllForTask(taskItemId, date) {
        return this.request('/Home/ResetAllForTask', 'POST', {
            taskItemId: taskItemId,
            date: date
        });
    }

    async completeAllForTask(taskItemId, date) {
        return this.request('/Home/CompleteAllForTask', 'POST', {
            taskItemId: taskItemId,
            date: date
        });
    }

    async saveAdjustment(branchId, taskItemId, date, adjustmentMinutes, adjustmentReason) {
        return this.request('/Home/SaveAdjustment', 'POST', {
            branchId: branchId,
            taskItemId: taskItemId,
            date: date,
            adjustmentMinutes: adjustmentMinutes,
            adjustmentReason: adjustmentReason
        });
    }

    async saveNotes(branchId, notes) {
        return this.request('/Home/SaveNotes', 'POST', {
            branchId: branchId,
            notes: notes
        });
    }

    async getDashboardStats(date) {
        return this.request(`/Home/GetDashboardStats?date=${date}`, 'GET');
    }
}

// Initialize Global API Service
window.apiService = new ApiService();
var apiService = window.apiService;

// ========== UI UPDATE FUNCTIONS (DOM Manipulation Optimized) ==========
function updateTaskCell(cell, data) {
    const button = cell.find('.task-button');
    if (!button.length) return;
    
    let bgColor = "bg-gray-50", 
        textColor = "text-gray-700", 
        borderColor = "border-gray-200",
        timeDisplay = "--:--", 
        statusDisplay = "Pending", 
        titleText = "Click to complete";
    
    if (data.isCompleted && data.completedAt) {
        const utcDate = new Date(data.completedAt);
        if (!isNaN(utcDate.getTime())) {
            let hours = utcDate.getHours();
            const minutes = utcDate.getMinutes();
            const ampm = hours >= 12 ? 'PM' : 'AM';
            hours = hours % 12 || 12;
            timeDisplay = `${hours}:${String(minutes).padStart(2, '0')} ${ampm}`;
            
            if (data.delayType === 'early') {
                bgColor = "bg-blue-900/60";
                textColor = "text-blue-300";
                borderColor = "border-blue-700/50";
                statusDisplay = data.delayText || "Early";
                titleText = data.delayText || "Completed early";
            } else if (data.isOnTime === true) {
                bgColor = "bg-emerald-900/60";
                textColor = "text-emerald-300";
                borderColor = "border-emerald-700/50";
                statusDisplay = "On Time";
                titleText = "Completed on time";
            } else if (data.delayType === 'late' || data.delayType === 'minutes' || data.delayType === 'hours' || data.delayType === 'days') {
                if (data.delayText) {
                    statusDisplay = data.delayText;
                    titleText = data.delayText;
                    if (data.delayText.includes('m') || data.delayText.includes('min')) {
                        bgColor = "bg-amber-900/60";
                        textColor = "text-amber-300";
                        borderColor = "border-amber-700/50";
                    } else if (data.delayText.includes('h') || data.delayText.includes('hr')) {
                        bgColor = "bg-orange-900/60";
                        textColor = "text-orange-300";
                        borderColor = "border-orange-700/50";
                    } else if (data.delayText.includes('d') || data.delayText.includes('day')) {
                        bgColor = "bg-red-900/60";
                        textColor = "text-red-300";
                        borderColor = "border-red-700/50";
                    } else {
                        bgColor = "bg-amber-900/60";
                        textColor = "text-amber-300";
                        borderColor = "border-amber-700/50";
                    }
                } else {
                    bgColor = "bg-amber-900/60";
                    textColor = "text-amber-300";
                    borderColor = "border-amber-700/50";
                    statusDisplay = "Late";
                    titleText = "Completed late";
                }
            } else {
                bgColor = "bg-red-900/60";
                textColor = "text-red-300";
                borderColor = "border-red-700/50";
                statusDisplay = data.delayText || "Late";
                titleText = data.delayText || "Completed late";
            }
        }
    } else {
        bgColor = "bg-slate-800/50";
        textColor = "text-slate-400";
        borderColor = "border-slate-700/50";
    }
    
    const buttonClasses = `w-full px-1 py-2 rounded text-[9px] font-medium ${bgColor} ${textColor} hover:opacity-90 transition-all flex items-center justify-between gap-0.5 border ${borderColor} task-button`;
    button.removeClass().addClass(buttonClasses).attr('title', titleText);
    
    let newHtml = `<span class="font-mono font-bold flex-1 time-display">${escapeHtml(timeDisplay)}</span>
                   <span class="flex-1 text-[8px] truncate px-0.5 status-display">${escapeHtml(statusDisplay)}</span>`;
    
    if (data.assignedTo) {
        const initial = data.assignedTo.charAt(0).toUpperCase();
        newHtml += `<span class="inline-flex items-center justify-center w-3.5 h-3.5 bg-indigo-600 text-white rounded-full text-[6px] font-bold flex-shrink-0 employee-initial" title="${escapeHtml(data.assignedTo)}">${escapeHtml(initial)}</span>`;
    }
    
    button.html(newHtml).prop('disabled', false);
    updateAdjustmentIndicator(cell, data.adjustmentMinutes);
}

function updateAdjustmentIndicator(cell, minutes) {
    const existing = cell.find('.adjustment-indicator');
    if (minutes > 0) {
        if (existing.length) {
            existing.attr('title', `+${minutes} min adjustment`);
        } else {
            cell.find('.relative').append(`<span class="absolute -top-1 -left-2 w-3.5 h-3.5 bg-amber-400 text-white rounded-full text-[5px] flex items-center justify-center shadow-sm z-10 adjustment-indicator" title="+${minutes} min adjustment"><i class="fas fa-clock"></i></span>`);
        }
    } else {
        existing.remove();
    }
}

function updateBranchProgress(row) {
    const progressCell = row.find('td:last-child');
    const visibleTasks = row.find('.task-button').length;
    const completedTasks = row.find('.task-button.bg-emerald-900\\/60, .task-button.bg-blue-900\\/60, .task-button.bg-amber-900\\/60, .task-button.bg-orange-900\\/60, .task-button.bg-red-900\\/60').length;
    const progress = visibleTasks > 0 ? Math.round((completedTasks * 100) / visibleTasks) : 0;
    progressCell.find('.bg-blue-500').css('width', progress + '%');
    progressCell.find('.progress-percent').text(progress + '%');
}

function updateStats() {
    let completed = 0, total = 0;
    $('.task-button').each(function() {
        total++;
        if ($(this).hasClass('bg-emerald-900/60') || $(this).hasClass('bg-blue-900/60') || 
            $(this).hasClass('bg-amber-900/60') || $(this).hasClass('bg-orange-900/60') || 
            $(this).hasClass('bg-red-900/60')) {
            completed++;
        }
    });
    
    $('#completedCount').text(completed);
    $('#pendingCount').text(total - completed);
    
    let onTimeCount = 0;
    $('.task-button').each(function() {
        if ($(this).hasClass('bg-emerald-900/60')) {
            onTimeCount++;
        }
    });
    const onTimeRate = total > 0 ? Math.round((onTimeCount / total) * 100) : 0;
    $('#onTimeRate').text(onTimeRate + '%');
    
    const completionRate = total > 0 ? Math.round((completed / total) * 100) : 0;
    $('#overallCompletionRate').text(`${completionRate}%`);
    $('#completionBar').css('width', `${completionRate}%`);
}

function updateNotesIcon(branchId, notes) {
    const icon = $(`.notes-btn-${branchId}`);
    const hasNotes = notes && notes.trim() !== '' && notes !== '""';
    if (hasNotes) {
        icon.html('<i class="fas fa-sticky-note text-sm text-blue-500"></i>');
        icon.attr('title', 'View/Edit notes');
    } else {
        icon.html('<i class="fas fa-plus-circle text-sm text-gray-400"></i>');
        icon.attr('title', 'Add notes');
    }
}

// ========== BATCH LOAD ALL TASK STATUSES ==========
async function loadAllTaskStatuses() {
    const date = DashboardState.currentDate;
    const cells = $('td[id^="cell-"]');
    
    if (!cells.length) return;
    
    const branchIds = [...new Set(cells.map((i, el) => $(el).data('branch')).get())];
    const taskIds = [...new Set(cells.map((i, el) => $(el).data('task')).get())];
    
    const branchChunks = [];
    for (let i = 0; i < branchIds.length; i += DashboardConstants.BATCH_API_CHUNK_SIZE) {
        branchChunks.push(branchIds.slice(i, i + DashboardConstants.BATCH_API_CHUNK_SIZE));
    }
    
    const allResults = {};
    
    for (const branchChunk of branchChunks) {
        try {
            const result = await apiService.getTaskStatusesBatch(branchChunk, taskIds, date);
            Object.assign(allResults, result);
        } catch (error) {
            console.error('Failed to load task statuses chunk:', error);
            showNotification('Error loading some task statuses', 'error');
        }
    }
    
    const updates = [];
    for (const cell of cells) {
        const $cell = $(cell);
        const branchId = $cell.data('branch');
        const taskId = $cell.data('task');
        const key = `${branchId}_${taskId}`;
        const data = allResults[key];
        
        if (data && data.exists) {
            updates.push({ cell: $cell, data: data });
        }
    }
    
    requestAnimationFrame(() => {
        updates.forEach(update => {
            updateTaskCell(update.cell, update.data);
        });
        updateStats();
    });
}

// ========== TREND DATA ==========
async function loadTrendData() {
    if (!DashboardState.previousDate) return;
    try {
        const data = await apiService.getDashboardStats(DashboardState.previousDate);
        if (data) {
            DashboardState.previousStats = {
                completed: data.completed || 0,
                pending: data.pending || 0,
                completionRate: data.completionRate || 0
            };
            updateTrendIndicators();
        }
    } catch (e) {
        console.log('Could not load trend data:', e);
    }
}

function updateTrendIndicators() {
    const currentCompleted = parseInt($('#completedCount').text()) || 0;
    const currentPending = parseInt($('#pendingCount').text()) || 0;
    const currentRate = currentCompleted + currentPending > 0 ? Math.round((currentCompleted / (currentCompleted + currentPending)) * 100) : 0;
    
    const completedDiff = currentCompleted - DashboardState.previousStats.completed;
    const completedTrendIcon = completedDiff >= 0 ? 'fa-arrow-up' : 'fa-arrow-down';
    const completedTrendColor = completedDiff >= 0 ? 'text-green-500' : 'text-red-500';
    $('#completedTrendValue').html(`${completedDiff >= 0 ? '+' : ''}${completedDiff}`);
    $('#completedTrend').html(`<i class="fas ${completedTrendIcon} ${completedTrendColor} text-[8px] mr-1"></i><span class="${completedTrendColor}">${completedDiff >= 0 ? '+' : ''}${completedDiff}</span>`);
    
    const pendingDiff = currentPending - DashboardState.previousStats.pending;
    const pendingTrendIcon = pendingDiff <= 0 ? 'fa-arrow-down' : 'fa-arrow-up';
    const pendingTrendColor = pendingDiff <= 0 ? 'text-green-500' : 'text-red-500';
    $('#pendingTrendValue').html(`${pendingDiff >= 0 ? '+' : ''}${pendingDiff}`);
    $('#pendingTrend').html(`<i class="fas ${pendingTrendIcon} ${pendingTrendColor} text-[8px] mr-1"></i><span class="${pendingTrendColor}">${pendingDiff >= 0 ? '+' : ''}${pendingDiff}</span>`);
    
    const rateDiff = currentRate - DashboardState.previousStats.completionRate;
    const rateTrendIcon = rateDiff >= 0 ? '↑' : '↓';
    const rateTrendColor = rateDiff >= 0 ? 'text-green-500' : 'text-red-500';
    $('#trendText').html(`<span class="${rateTrendColor}">${rateTrendIcon} ${Math.abs(rateDiff)}% vs yesterday</span>`);
}

// ========== TASK ACTIONS WITH ERROR BOUNDARIES ==========
async function quickComplete(branchId, taskId, date, button) {
    const cell = $(button).closest('td');
    const row = cell.closest('tr');
    const originalHtml = button.innerHTML;
    
    try {
        button.innerHTML = '<i class="fas fa-spinner fa-spin text-xs"></i>';
        button.disabled = true;
        
        const now = new Date();
        const year = now.getFullYear();
        const month = String(now.getMonth() + 1).padStart(2, '0');
        const day = String(now.getDate()).padStart(2, '0');
        const hours = String(now.getHours()).padStart(2, '0');
        const minutes = String(now.getMinutes()).padStart(2, '0');
        const localDateTime = `${year}-${month}-${day}T${hours}:${minutes}`;
        
        const res = await apiService.updateTaskTime(branchId, taskId, date, localDateTime);
        
        if (res.success && res.taskData) {
            updateTaskCell(cell, res.taskData);
            updateBranchProgress(row);
            updateStats();
            showNotification('Task completed successfully', 'success');
        } else {
            throw new Error(res.message || 'Failed to complete task');
        }
    } catch (error) {
        console.error('Quick complete error:', error);
        showNotification(error.message || 'Error completing task', 'error');
        button.innerHTML = originalHtml;
        button.disabled = false;
    }
}

async function resetSingle(branchId, taskId, date, event, button) {
    event.stopPropagation();
    if (!confirm('Reset this specific task for this branch?')) return;
    
    const cell = $(button).closest('td');
    const row = cell.closest('tr');
    const originalHtml = $(button).html();
    
    try {
        $(button).html('<i class="fas fa-spinner fa-spin text-xs"></i>').prop('disabled', true);
        
        const res = await apiService.resetTask(branchId, taskId, date);
        
        if (res.success) {
            const fresh = await apiService.getTaskStatusesBatch([branchId], [taskId], date);
            const key = `${branchId}_${taskId}`;
            if (fresh[key] && fresh[key].exists) {
                updateTaskCell(cell, fresh[key]);
                updateBranchProgress(row);
                updateStats();
            }
            showNotification('Task reset successfully', 'success');
        } else {
            throw new Error(res.message || 'Failed to reset task');
        }
    } catch (error) {
        console.error('Reset single error:', error);
        showNotification(error.message || 'Error resetting task', 'error');
        $(button).html(originalHtml).prop('disabled', false);
    }
}

async function resetAllForTask(taskId, taskName, event) {
    if (event) event.stopPropagation();
    if (!confirm(`Reset ALL "${taskName}" tasks for all branches?`)) return;
    
    const btn = event ? event.currentTarget : null;
    const originalHtml = btn ? btn.innerHTML : '';
    
    try {
        if (btn) {
            btn.innerHTML = '<i class="fas fa-spinner fa-spin text-xs"></i>';
            btn.disabled = true;
        }
        
        const res = await apiService.resetAllForTask(taskId, DashboardState.currentDate);
        
        if (res.success) {
            showNotification(`${res.count} tasks reset successfully`, 'success');
            await loadAllTaskStatuses();
        } else {
            throw new Error(res.message || 'Failed to reset tasks');
        }
    } catch (error) {
        console.error('Reset all for task error:', error);
        showNotification(error.message || 'Error resetting tasks', 'error');
    } finally {
        if (btn) {
            btn.innerHTML = originalHtml;
            btn.disabled = false;
        }
    }
}

async function completeAllForTask(taskId, taskName, event) {
    if (event) event.stopPropagation();
    if (!confirm(`Complete ALL "${taskName}" tasks for all branches?`)) return;
    
    const btn = event ? event.currentTarget : null;
    const originalHtml = btn ? btn.innerHTML : '';
    
    try {
        if (btn) {
            btn.innerHTML = '<i class="fas fa-spinner fa-spin text-xs"></i>';
            btn.disabled = true;
        }
        
        const res = await apiService.completeAllForTask(taskId, DashboardState.currentDate);
        
        if (res.success) {
            showNotification(`${res.count} tasks completed successfully`, 'success');
            await loadAllTaskStatuses();
        } else {
            throw new Error(res.message || 'Failed to complete tasks');
        }
    } catch (error) {
        console.error('Complete all for task error:', error);
        showNotification(error.message || 'Error completing tasks', 'error');
    } finally {
        if (btn) {
            btn.innerHTML = originalHtml;
            btn.disabled = false;
        }
    }
}

// ========== NOTES FUNCTIONS ==========
function openNotesModalFromData(element) {
    const $button = $(element);
    const branchId = $button.data('branch-id');
    const branchName = $button.data('branch-name');
    let currentNotes = $button.data('branch-notes');
    
    let decodedNotes = '';
    if (currentNotes && currentNotes !== '""' && currentNotes !== 'null' && currentNotes !== '') {
        let cleanedNotes = currentNotes.toString();
        if ((cleanedNotes.startsWith('"') && cleanedNotes.endsWith('"')) ||
            (cleanedNotes.startsWith("'") && cleanedNotes.endsWith("'"))) {
            cleanedNotes = cleanedNotes.slice(1, -1);
        }
        decodedNotes = cleanedNotes.replace(/&quot;/g, '"').replace(/&#39;/g, "'");
    }
    
    $('#notesBranchId').val(branchId);
    $('#notesBranchName').text(branchName);
    $('#notesContent').val(decodedNotes);
    $('#notesModal').removeClass('hidden').addClass('flex');
}

async function saveNotes() {
    const branchId = $('#notesBranchId').val();
    const notes = $('#notesContent').val();
    
    try {
        const res = await apiService.saveNotes(branchId, notes);
        
        if (res.success) {
            const notesButton = $(`.notes-btn-${branchId}`);
            notesButton.data('branch-notes', notes);
            notesButton.attr('data-branch-notes', notes);
            
            if (notes && notes.trim() !== '') {
                notesButton.html('<i class="fas fa-sticky-note text-sm text-blue-500"></i>');
            } else {
                notesButton.html('<i class="fas fa-plus-circle text-sm text-gray-400"></i>');
            }
            
            showNotification(notes ? 'Notes saved successfully' : 'Notes removed', 'success');
            closeNotesModal();
        } else {
            throw new Error(res.message || 'Failed to save notes');
        }
    } catch (error) {
        console.error('Save notes error:', error);
        showNotification(error.message || 'Error saving notes', 'error');
    }
}

function closeNotesModal() {
    $('#notesModal').removeClass('flex').addClass('hidden');
}

// ========== BULK UPDATE FUNCTIONS ==========
function openBulkUpdateModal() {
    $('#bulkUpdateModal').removeClass('hidden').addClass('flex');
    setBulkTime('now');
}

function closeBulkUpdateModal() {
    $('#bulkUpdateModal').removeClass('flex').addClass('hidden');
}

function setBulkTime(preset) {
    const d = new Date();
    switch(preset) {
        case 'morning': d.setHours(8, 0, 0, 0); break;
        case 'noon': d.setHours(12, 0, 0, 0); break;
        case 'afternoon': d.setHours(16, 0, 0, 0); break;
        case 'evening': d.setHours(20, 0, 0, 0); break;
        case 'night': d.setHours(23, 0, 0, 0); break;
        default: break;
    }
    
    const year = d.getFullYear();
    const month = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    const hours = String(d.getHours()).padStart(2, '0');
    const minutes = String(d.getMinutes()).padStart(2, '0');
    $('#bulkCompletionTime').val(`${year}-${month}-${day}T${hours}:${minutes}`);
}

async function executeBulkUpdate() {
    const taskId = $('#bulkTaskId').val();
    const time = $('#bulkCompletionTime').val();
    
    if (!taskId || !time) {
        showNotification('Select a task and time', 'error');
        return;
    }
    
    // Get all branch IDs from the table rows
    const branchIds = [];
    $('.branch-row').each(function() {
        const branchId = $(this).attr('data-branch-id');
        if (branchId) branchIds.push(parseInt(branchId));
    });
    
    if (branchIds.length === 0) {
        showNotification('No branches found on page', 'error');
        return;
    }
    
    console.log('Bulk updating task:', taskId, 'time:', time, 'branches:', branchIds);
    console.log('Token:', DashboardState.token);
    
    const btn = $('#bulkUpdateModal button:contains("EXECUTE")');
    const originalText = btn.text();
    
    try {
        btn.text('Updating...').prop('disabled', true);
        
        const payload = {
            taskItemId: parseInt(taskId),
            completionDateTime: time,
            branchIds: branchIds
        };
        console.log('Sending payload:', JSON.stringify(payload));
        
        const response = await fetch('/Home/BulkUpdate', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': DashboardState.token
            },
            body: JSON.stringify(payload)
        });
        
        const res = await response.json();
        console.log('Response:', res);
        
        if (res.success) {
            showNotification(`${res.count} tasks updated successfully`, 'success');
            closeBulkUpdateModal();
            await loadAllTaskStatuses();
        } else {
            throw new Error(res.message || 'Failed to update tasks');
        }
    } catch (error) {
        console.error('Bulk update error:', error);
        showNotification(error.message || 'Error during bulk update', 'error');
    } finally {
        btn.text(originalText).prop('disabled', false);
    }
}

// ========== TIME PICKER FUNCTIONS ==========
function openTimePicker(branchId, taskId, date, button) {
    $('#timePickerBranchId').val(branchId);
    $('#timePickerTaskId').val(taskId);
    $('#timePickerDate').val(date);
    $('#timePickerTaskInfo').text('Editing completion time');
    
    const now = new Date();
    const year = now.getFullYear();
    const month = String(now.getMonth() + 1).padStart(2, '0');
    const day = String(now.getDate()).padStart(2, '0');
    const hours = String(now.getHours()).padStart(2, '0');
    const minutes = String(now.getMinutes()).padStart(2, '0');
    $('#timePickerDateTime').val(`${year}-${month}-${day}T${hours}:${minutes}`);
    
    $('#timePickerModal').removeClass('hidden').addClass('flex');
    updateTimePreview();
}

function closeTimePicker() {
    $('#timePickerModal').removeClass('flex').addClass('hidden');
}

function updateTimePreview() {
    const dateTimeStr = $('#timePickerDateTime').val();
    if (!dateTimeStr) return;
    try {
        const localDate = new Date(dateTimeStr);
        if (isNaN(localDate.getTime())) return;
        let hours = localDate.getHours();
        const minutes = localDate.getMinutes();
        const ampm = hours >= 12 ? 'PM' : 'AM';
        const displayHours = hours % 12 || 12;
        const timeStr = `${displayHours}:${String(minutes).padStart(2, '0')} ${ampm}`;
        $('#timePickerPreview').html(`<i class="fas fa-info-circle mr-1"></i> Selected: ${timeStr} on ${localDate.toLocaleDateString()}`);
    } catch (e) {}
}

function setTimePreset(preset) {
    const date = new Date();
    switch(preset) {
        case 'now': break;
        case 'morning': date.setHours(8, 0, 0, 0); break;
        case 'noon': date.setHours(12, 0, 0, 0); break;
        case 'afternoon': date.setHours(16, 0, 0, 0); break;
        case 'evening': date.setHours(20, 0, 0, 0); break;
        case 'night': date.setHours(23, 0, 0, 0); break;
    }
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    const hours = String(date.getHours()).padStart(2, '0');
    const minutes = String(date.getMinutes()).padStart(2, '0');
    $('#timePickerDateTime').val(`${year}-${month}-${day}T${hours}:${minutes}`);
    updateTimePreview();
}

async function saveTaskTime() {
    const branchId = $('#timePickerBranchId').val();
    const taskId = $('#timePickerTaskId').val();
    const date = $('#timePickerDate').val();
    const dateTimeStr = $('#timePickerDateTime').val();
    const cell = $('#cell-' + branchId + '-' + taskId);
    const row = cell.closest('tr');
    
    if (!dateTimeStr) {
        showNotification('Please select a time', 'error');
        return;
    }
    
    try {
        const res = await apiService.updateTaskTime(branchId, taskId, date, dateTimeStr);
        
        if (res.success && res.taskData) {
            updateTaskCell(cell, res.taskData);
            updateBranchProgress(row);
            updateStats();
            showNotification('Time updated successfully', 'success');
            closeTimePicker();
        } else {
            throw new Error(res.message || 'Failed to update time');
        }
    } catch (error) {
        console.error('Save task time error:', error);
        showNotification(error.message || 'Error updating time', 'error');
    }
}

// ========== ADJUSTMENT FUNCTIONS ==========
function openAdjustmentModal(branchId, taskId, taskName, date, button) {
    $('#adjustmentBranchId').val(branchId);
    $('#adjustmentTaskId').val(taskId);
    $('#adjustmentDate').val(date);
    $('#adjustmentModalTitle').text('Adjust: ' + taskName);
    
    apiService.getTaskStatusesBatch([branchId], [taskId], date)
        .then(res => {
            const key = `${branchId}_${taskId}`;
            if (res[key] && res[key].exists && res[key].adjustmentMinutes > 0) {
                $('#adjustmentMinutes').val(res[key].adjustmentMinutes);
                $('#adjustmentReason').val(res[key].adjustmentReason || '');
                $('#currentAdjustmentDisplay').text(res[key].adjustmentMinutes);
            } else {
                $('#adjustmentMinutes').val(0);
                $('#adjustmentReason').val('');
                $('#currentAdjustmentDisplay').text('0');
            }
        })
        .catch(() => {
            $('#adjustmentMinutes').val(0);
            $('#adjustmentReason').val('');
            $('#currentAdjustmentDisplay').text('0');
        });
    
    $('#adjustmentModal').removeClass('hidden').addClass('flex');
}

function closeAdjustmentModal() {
    $('#adjustmentModal').removeClass('flex').addClass('hidden');
}

function addAdjustment(min) {
    const current = parseInt($('#adjustmentMinutes').val()) || 0;
    $('#adjustmentMinutes').val(current + min);
    $('#currentAdjustmentDisplay').text(current + min);
}

function clearAdjustment() {
    $('#adjustmentMinutes').val(0);
    $('#adjustmentReason').val('');
    $('#currentAdjustmentDisplay').text('0');
}

async function saveAdjustment() {
    const branchId = $('#adjustmentBranchId').val();
    const taskId = $('#adjustmentTaskId').val();
    const date = $('#adjustmentDate').val();
    const minutes = parseInt($('#adjustmentMinutes').val()) || 0;
    const reason = $('#adjustmentReason').val();
    const cell = $('#cell-' + branchId + '-' + taskId);
    const row = cell.closest('tr');
    
    const btn = $('#adjustmentModal .bg-amber-500');
    const originalText = btn.text();
    
    try {
        btn.text('Saving...').prop('disabled', true);
        
        const response = await fetch('/Home/SaveAdjustment', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'RequestVerificationToken': DashboardState.token
            },
            body: new URLSearchParams({
                branchId: branchId,
                taskItemId: taskId,
                date: date,
                adjustmentMinutes: minutes,
                adjustmentReason: reason
            })
        });
        
        const res = await response.json();
        
        if (res.success) {
            const fresh = await apiService.getTaskStatusesBatch([branchId], [taskId], date);
            const key = `${branchId}_${taskId}`;
            if (fresh[key] && fresh[key].exists) {
                updateTaskCell(cell, fresh[key]);
                updateBranchProgress(row);
                updateStats();
            }
            showNotification(minutes === 0 ? 'Adjustment removed' : 'Adjustment saved', 'success');
            closeAdjustmentModal();
        } else {
            throw new Error(res.message || 'Failed to save adjustment');
        }
    } catch (error) {
        console.error('Save adjustment error:', error);
        showNotification(error.message || 'Error saving adjustment', 'error');
    } finally {
        btn.text(originalText).prop('disabled', false);
    }
}

// ========== EXPORT FUNCTIONS ==========
function exportDashboardToExcel() {
    const data = [];
    const headers = ['Branch'];
    
    $('.task-header').each(function() {
        headers.push($(this).find('span:first').text().trim());
    });
    headers.push('Completion %', 'Assigned To', 'Notes');
    
    $('#dashboardTableBody tr:visible').each(function() {
        const $row = $(this);
        const rowData = {};
        
        rowData['Branch'] = $row.find('td:first .text-xs').text().trim();
        
        let taskIndex = 0;
        $row.find('.task-cell:not(.filtered-out)').each(function() {
            const taskName = headers[taskIndex + 1];
            if (taskName) {
                rowData[taskName] = $(this).find('.status-display').text();
            }
            taskIndex++;
        });
        
        rowData['Completion %'] = $row.find('.progress-percent').text();
        rowData['Assigned To'] = $row.find('[id^="assignedTo-"]').text().trim() || 'Unassigned';
        rowData['Notes'] = '';
        data.push(rowData);
    });
    
    const ws = XLSX.utils.json_to_sheet(data);
    const wb = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(wb, ws, 'Dashboard');
    XLSX.writeFile(wb, 'Dashboard_' + DashboardState.currentDate + '.xlsx');
}

// ========== KEYBOARD SHORTCUTS ==========
function showKeyboardShortcuts() {
    alert('🎹 Keyboard Shortcuts:\n\n' +
          'Ctrl + D: Next day\n' +
          'Ctrl + A: Previous day\n' +
          'Ctrl + T: Today\n' +
          'Ctrl + B: Bulk update\n' +
          'Ctrl + H: This help\n\n' +
          'Tip: Hover over any task button for additional actions');
}

function changeDate(days) {
    const datePicker = $('#datePicker');
    const currentDate = datePicker.val();
    if (currentDate) {
        const date = new Date(currentDate);
        date.setDate(date.getDate() + days);
        const newDate = date.toISOString().split('T')[0];
        window.location.href = '/Home/Index?date=' + newDate;
    }
}

// ========== FILTER HIDDEN TASKS ==========
function filterHiddenTasks() {
    $('.task-cell').each(function() {
        const $cell = $(this);
        const isHidden = $cell.closest('td').find('.text-gray-400').length > 0;
        if (isHidden) {
            $cell.addClass('filtered-out');
        }
    });
}

// ========== INITIALIZATION ==========
$(document).ready(function() {
    console.log('Dashboard initializing...');
    
    DashboardState.token = $('#requestVerificationToken').val();
    DashboardState.currentDate = $('#currentDate').val();
    DashboardState.isHoliday = $('#isHoliday').val() === 'true';
    DashboardState.holidayName = $('#holidayName').val();
    DashboardState.previousDate = $('#previousDate').val();
    DashboardState.branchNotes = window.branchNotes || {};
    
    apiService.setToken(DashboardState.token);
    
    $('#skeletonLoader').show();
    $('#dashboardContainer').hide();
    
    $('#datePicker').on('change', function() {
        const selectedDate = $(this).val();
        if (selectedDate) {
            window.location.href = '/Home/Index?date=' + selectedDate;
        }
    });
    
    $('#timePickerDateTime').on('change', updateTimePreview);
    
    $(document).keydown(function(e) {
        if (e.ctrlKey && e.key === 'd') { e.preventDefault(); changeDate(1); }
        if (e.ctrlKey && e.key === 'a') { e.preventDefault(); changeDate(-1); }
        if (e.ctrlKey && e.key === 't') { e.preventDefault(); window.location.href = '/Home/Index'; }
        if (e.ctrlKey && e.key === 'b') { e.preventDefault(); openBulkUpdateModal(); }
        if (e.ctrlKey && e.key === 'h') { e.preventDefault(); showKeyboardShortcuts(); }
        if (e.key === 'Escape') {
            closeTimePicker();
            closeAdjustmentModal();
            closeNotesModal();
            closeBulkUpdateModal();
        }
    });
    
    setTimeout(function() {
        loadTrendData();
        filterHiddenTasks();
        
        $('#skeletonLoader').fadeOut(DashboardConstants.SKELETON_FADE_MS, function() {
            $('#dashboardContainer').fadeIn(DashboardConstants.SKELETON_FADE_MS);
        });
    }, 500);
    
    loadAllTaskStatuses();
    
    console.log('Dashboard initialized');
});