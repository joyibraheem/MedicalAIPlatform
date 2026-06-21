/**
 * ChestAI assistant UI: persistence (localStorage), job polling, notifications (SW + Notification).
 */
(function () {
    'use strict';

    var pendingUnread = 0;
    var savedMessagesScrollTop = 0;
    var lastBadgeValue = 0;

    function $(id) {
        return document.getElementById(id);
    }

    function pad(n) {
        return n < 10 ? '0' + n : '' + n;
    }

    function formatClock(isoOrDate) {
        var d = typeof isoOrDate === 'string' ? new Date(isoOrDate) : isoOrDate;
        if (isNaN(d.getTime())) d = new Date();
        return pad(d.getHours()) + ':' + pad(d.getMinutes());
    }

    function escapeHtml(s) {
        var div = document.createElement('div');
        div.textContent = s == null ? '' : String(s);
        return div.innerHTML;
    }

    function formatAiBodyPlain(text) {
        if (!text) return '';
        var escaped = escapeHtml(text);
        return escaped.replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>').replace(/\n/g, '<br/>');
    }

    function chestIconSrc() {
        return typeof window.__maiChestIconUrl === 'string' && window.__maiChestIconUrl.length > 0
            ? window.__maiChestIconUrl
            : '/images/chest-icon.png';
    }

    function startOfDayKey(isoOrDate) {
        var d = typeof isoOrDate === 'string' ? new Date(isoOrDate) : isoOrDate;
        if (isNaN(d.getTime())) return 0;
        return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
    }

    function formatDayDivider(isoOrDate) {
        var d = typeof isoOrDate === 'string' ? new Date(isoOrDate) : isoOrDate;
        if (isNaN(d.getTime())) return '';
        var todayK = startOfDayKey(new Date());
        var yK = startOfDayKey(new Date(Date.now() - 86400000));
        var k = startOfDayKey(d);
        if (k === todayK) return 'Today';
        if (k === yK) return 'Yesterday';
        try {
            return d.toLocaleDateString(undefined, { weekday: 'long', month: 'short', day: 'numeric' });
        } catch (_) {
            return pad(d.getMonth() + 1) + '/' + pad(d.getDate());
        }
    }

    function safePreviewUrl(u) {
        if (!u || typeof u !== 'string') return null;
        if (u.indexOf('data:') === 0 && u.length > 12000) return null;
        return u;
    }

    function bubbleModifierClasses(entry) {
        var k = entry.kind || '';
        var classes = [];
        if (k === 'ct-result' || k === 'xray-result')
            classes.push('mai-chat-msg--card', 'mai-chat-msg--card-success');
        else if (k === 'ct-error' || k === 'assistant-error' || k === 'xray-error')
            classes.push('mai-chat-msg--card', 'mai-chat-msg--card-danger');
        else if (k === 'ct-queued' || k === 'xray-queued') classes.push('mai-chat-msg--pill', 'mai-chat-msg--pill-queue');
        return classes.join(' ');
    }

    function cardHeadLabel(kind) {
        if (kind === 'ct-result' || kind === 'xray-result') return 'Analysis complete';
        if (kind === 'ct-error') return 'CT analysis issue';
        if (kind === 'xray-error') return 'X-ray analysis issue';
        if (kind === 'assistant-error') return 'Assistant issue';
        return 'Update';
    }

    function messagesApiUrl() {
        return typeof window.__maiChatMessagesUrl === 'string' && window.__maiChatMessagesUrl.length > 0
            ? window.__maiChatMessagesUrl
            : '/api/assistant/chat/messages';
    }

    function serverRowToEntry(m) {
        var meta = {};
        try {
            meta = m.metadataJson ? JSON.parse(m.metadataJson) : {};
        } catch (_) {
            meta = {};
        }
        var role = (m.role || 'assistant').toLowerCase();
        var type = role === 'user' ? 'user' : role === 'system' ? 'system' : 'ai';
        var raw = m.content || '';
        var htmlBody = type === 'user' ? escapeHtml(raw) : formatAiBodyPlain(raw);
        var redirect = meta.redirect || null;
        var link =
            redirect &&
            '<a href="' +
                escapeHtml(redirect) +
                '">Open full results →</a>';
        var previewUrl = meta.previewUrl || meta.previewImageUrl || meta.thumbnailUrl || null;
        return {
            id: 'db-' + String(m.createdAt || '') + '-' + String(Math.random()).slice(2),
            type: type,
            htmlBody: htmlBody,
            timestamp: m.createdAt || new Date().toISOString(),
            jobId: m.relatedJobId || null,
            kind: meta.kind || null,
            actionHtml: link,
            previewUrl: previewUrl
        };
    }

    function setSkeletonVisible(visible) {
        var sk = $('mai-chat-skeleton');
        if (!sk) return;
        if (visible) sk.removeAttribute('hidden');
        else sk.setAttribute('hidden', '');
    }

    function updateOnboardingVisibility(messageCount) {
        var ob = $('mai-chat-onboarding');
        if (!ob) return;
        ob.hidden = messageCount > 0;
    }

    function pulseHighlightUnread() {
        var area = $('mai-chat-scroll-area');
        if (!area) return;
        area.classList.remove('mai-chat-highlight-flash');
        void area.offsetWidth;
        area.classList.add('mai-chat-highlight-flash');
        setTimeout(function () {
            area.classList.remove('mai-chat-highlight-flash');
        }, 1200);
    }

    function appendDayDivider(wrap, label) {
        if (!label) return;
        var d = document.createElement('div');
        d.className = 'mai-chat-day-divider';
        d.setAttribute('role', 'separator');
        d.innerHTML = '<span>' + escapeHtml(label) + '</span>';
        wrap.appendChild(d);
    }

    function refreshFloatingChatFromServer(done, renderOpts) {
        fetch(messagesApiUrl() + '?take=320', {
            credentials: 'same-origin',
            headers: { Accept: 'application/json' }
        })
            .then(function (r) {
                if (!r.ok) throw new Error('messages ' + r.status);
                return r.json();
            })
            .then(function (data) {
                var rows = (data && data.messages) || [];
                var entries = rows.map(serverRowToEntry);
                window.MedicalAiChatStorage.setMessagesFromApi(entries);
                renderStoredMessages(renderOpts || {});
                syncTypingFromActivePolls();
                if (typeof done === 'function') done();
            })
            .catch(function (e) {
                console.warn('[MaiChat] server refresh failed', e);
                renderStoredMessages(renderOpts || {});
                if (typeof done === 'function') done();
            });
    }

    function scrollMessagesContainerToBottom() {
        var area = $('mai-chat-scroll-area');
        if (!area) return;
        requestAnimationFrame(function () {
            area.scrollTop = area.scrollHeight;
        });
    }

    function restoreMessagesScroll() {
        var area = $('mai-chat-scroll-area');
        if (!area) return;
        requestAnimationFrame(function () {
            var max = Math.max(0, area.scrollHeight - area.clientHeight);
            area.scrollTop = Math.min(savedMessagesScrollTop, max);
        });
    }

    function setTyping(visible, label) {
        var row = $('mai-chat-typing');
        var lbl = $('mai-chat-typing-label');
        if (!row) return;
        if (label && lbl) lbl.textContent = label;
        row.hidden = !visible;
        var launcher = $('mai-chat-launcher');
        if (launcher) launcher.classList.toggle('mai-chat-is-busy', !!visible);
        scrollMessagesContainerToBottom();
    }

    function playSoftPing() {
        try {
            if (localStorage.getItem('maiQuiet') === '1') return;
        } catch (_) {}
        try {
            var Ctx = window.AudioContext || window.webkitAudioContext;
            if (!Ctx) return;
            var ctx = new Ctx();
            var master = ctx.createGain();
            master.connect(ctx.destination);
            master.gain.value = 1;
            var t0 = ctx.currentTime;

            function tone(freqHz, delaySec, peak, decaySec) {
                var o = ctx.createOscillator();
                var g = ctx.createGain();
                o.type = 'sine';
                o.frequency.value = freqHz;
                o.connect(g);
                g.connect(master);
                var s = t0 + delaySec;
                g.gain.setValueAtTime(0.0001, s);
                g.gain.exponentialRampToValueAtTime(peak, s + 0.018);
                g.gain.exponentialRampToValueAtTime(0.0001, s + decaySec);
                o.start(s);
                o.stop(s + decaySec + 0.03);
            }

            tone(587.33, 0, 0.048, 0.16);
            tone(783.99, 0.11, 0.038, 0.2);

            ctx.resume().catch(function () {});
            setTimeout(function () {
                try {
                    ctx.close();
                } catch (_) {}
            }, 520);
        } catch (_) {}
    }

    function playIncomingUnreadPing() {
        try {
            if (localStorage.getItem('maiQuiet') === '1') return;
        } catch (_) {}
        try {
            var Ctx = window.AudioContext || window.webkitAudioContext;
            if (!Ctx) return;
            var ctx = new Ctx();
            var o = ctx.createOscillator();
            var g = ctx.createGain();
            o.type = 'sine';
            o.connect(g);
            g.connect(ctx.destination);
            var t0 = ctx.currentTime;
            o.frequency.setValueAtTime(1046.5, t0);
            o.frequency.exponentialRampToValueAtTime(784, t0 + 0.09);
            g.gain.setValueAtTime(0.0001, t0);
            g.gain.exponentialRampToValueAtTime(0.04, t0 + 0.02);
            g.gain.exponentialRampToValueAtTime(0.0001, t0 + 0.22);
            o.start(t0);
            o.stop(t0 + 0.25);
            ctx.resume().catch(function () {});
            setTimeout(function () {
                try {
                    ctx.close();
                } catch (_) {}
            }, 400);
        } catch (_) {}
    }

    function attachNotificationBehaviors(notification) {
        if (!notification) return;
        notification.onclick = function () {
            try {
                window.focus();
            } catch (_) {}
            try {
                notification.close();
            } catch (_) {}
            try {
                if (
                    window.MedicalAiAssistantChat &&
                    typeof window.MedicalAiAssistantChat.openChat === 'function'
                ) {
                    window.MedicalAiAssistantChat.openChat();
                }
            } catch (_) {}
        };
    }

    function notifyAnalysisFinished(jobId, success, detail) {
        var title = success ? 'ChestAI — Analysis ready' : 'ChestAI — Analysis failed';
        var body = success
            ? 'Your scan finished. Open the chat bubble to review results.'
            : String(detail || 'Something went wrong. Open the chat bubble for details.').slice(0, 180);

        var icon = chestIconSrc();
        var tag = (success ? 'mai-ct-ok-' : 'mai-ct-fail-') + jobId;
        var sessKey = (success ? 'mai.notifyDone.' : 'mai.notifyFail.') + jobId;

        try {
            if (sessionStorage.getItem(sessKey)) {
                console.info('[MaiChat] skip duplicate notification session', tag);
                return;
            }
        } catch (_) {}

        playSoftPing();

        if (!('Notification' in window) || Notification.permission !== 'granted') {
            console.info('[MaiChat] notification skipped (no permission)');
            return;
        }

        function markShown() {
            try {
                sessionStorage.setItem(sessKey, '1');
            } catch (_) {}
        }

        function showViaPageApi() {
            try {
                var n = new Notification(title, {
                    body: body,
                    icon: icon,
                    tag: tag,
                    renotify: true
                });
                attachNotificationBehaviors(n);
                markShown();
                console.info('[MaiChat] notification shown (page)', tag);
            } catch (e) {
                console.warn('[MaiChat] notification error', e);
            }
        }

        try {
            if ('serviceWorker' in navigator) {
                navigator.serviceWorker.ready
                    .then(function (reg) {
                        return reg.showNotification(title, {
                            body: body,
                            icon: icon,
                            tag: tag,
                            renotify: true,
                            requireInteraction: false
                        });
                    })
                    .then(function () {
                        markShown();
                        console.info('[MaiChat] notification shown (SW)', tag);
                    })
                    .catch(function () {
                        showViaPageApi();
                    });
            } else {
                showViaPageApi();
            }
        } catch (e) {
            console.warn('[MaiChat] notification error', e);
        }
    }

    function renderMessageEntry(entry, renderOpts) {
        renderOpts = renderOpts || {};
        var wrap = $('mai-chat-messages');
        if (!wrap || !entry) return;

        var role =
            entry.type === 'user' ? 'user' : entry.type === 'system' ? 'system' : 'ai';
        var row = document.createElement('div');
        row.className = 'mai-chat-msg-row mai-chat-msg-row--' + role;
        if (renderOpts.highlightId && entry.id === renderOpts.highlightId) {
            row.classList.add('mai-chat-msg-row--fresh');
        }

        var bubbleExtra = bubbleModifierClasses(entry);
        var article = document.createElement('article');
        article.className = ('mai-chat-msg ' + role + ' ' + bubbleExtra).trim();
        article.setAttribute('role', 'article');
        if (entry.id) article.dataset.msgId = entry.id;

        var kind = entry.kind || '';
        var timeHtml =
            '<div class="mai-chat-msg-meta"><span class="mai-chat-msg-time">' +
            escapeHtml(formatClock(entry.timestamp)) +
            '</span></div>';

        var inner = '';
        var pv = safePreviewUrl(entry.previewUrl);

        if (kind === 'ct-result' || kind === 'ct-error' || kind === 'assistant-error') {
            if (pv && role !== 'user') {
                inner +=
                    '<div class="mai-chat-msg-media"><img src="' +
                    escapeHtml(pv) +
                    '" alt="" loading="lazy" decoding="async"/></div>';
            }
            inner +=
                '<div class="mai-chat-card-head"><span>' +
                escapeHtml(cardHeadLabel(kind)) +
                '</span></div>';
            inner += '<div class="mai-chat-card-body"><div class="mai-chat-msg-body">' + entry.htmlBody + '</div></div>';
            if (entry.actionHtml) inner += '<div class="mai-chat-msg-actions">' + entry.actionHtml + '</div>';
            inner += timeHtml;
        } else if (kind === 'ct-queued') {
            inner += '<span class="mai-chat-msg-label">Processing</span>';
            inner += '<div class="mai-chat-msg-body">' + entry.htmlBody + '</div>';
            inner += timeHtml;
        } else {
            inner += '<div class="mai-chat-msg-body">' + entry.htmlBody + '</div>';
            if (pv && role !== 'user') {
                inner +=
                    '<div class="mai-chat-msg-media" style="margin:10px 0 0;border-radius:12px;"><img src="' +
                    escapeHtml(pv) +
                    '" alt="" loading="lazy" decoding="async"/></div>';
            }
            if (entry.actionHtml) inner += '<div class="mai-chat-msg-actions">' + entry.actionHtml + '</div>';
            inner += timeHtml;
        }

        article.innerHTML = inner;

        if (role === 'ai') {
            var av = document.createElement('div');
            av.className = 'mai-chat-avatar';
            av.setAttribute('aria-hidden', 'true');
            av.innerHTML = '<img src="' + escapeHtml(chestIconSrc()) + '" alt="" loading="lazy"/>';
            row.appendChild(av);
            row.appendChild(article);
        } else if (role === 'system') {
            row.appendChild(article);
        } else {
            row.appendChild(article);
        }

        wrap.appendChild(row);
    }

    function appendAndPersist(type, htmlBody, meta) {
        meta = meta || {};
        var id =
            typeof crypto !== 'undefined' && crypto.randomUUID
                ? crypto.randomUUID()
                : 'm-' + Date.now() + '-' + Math.floor(Math.random() * 1e6);

        var entry = {
            id: id,
            type: type,
            htmlBody: htmlBody,
            timestamp: new Date().toISOString(),
            jobId: meta.jobId || null,
            kind: meta.kind || null,
            actionHtml: meta.actionHtml || null,
            previewUrl: meta.previewUrl || null
        };

        window.MedicalAiChatStorage.appendMessage(entry);
        renderStoredMessages({ scrollBottom: true });
        console.info('[MaiChat] message persisted', type, meta.kind || '');
        return entry;
    }

    function renderStoredMessages(renderOpts) {
        renderOpts = renderOpts || {};
        var wrap = $('mai-chat-messages');
        if (!wrap) return;
        wrap.innerHTML = '';
        var list = window.MedicalAiChatStorage.loadMessages().slice().sort(function (a, b) {
            return new Date(a.timestamp || 0).getTime() - new Date(b.timestamp || 0).getTime();
        });

        updateOnboardingVisibility(list.length);

        var highlightId = null;
        if (renderOpts.scrollBottom && list.length) {
            for (var i = list.length - 1; i >= 0; i--) {
                if (list[i].type !== 'user') {
                    highlightId = list[i].id;
                    break;
                }
            }
        }

        var lastDay = null;
        list.forEach(function (entry) {
            var dk = startOfDayKey(entry.timestamp);
            if (lastDay !== dk) {
                lastDay = dk;
                appendDayDivider(wrap, formatDayDivider(entry.timestamp));
            }
            renderMessageEntry(entry, { highlightId: highlightId });
        });

        syncTypingFromActivePolls();

        if (renderOpts.scrollBottom === false) {
            /* caller runs restoreMessagesScroll */
        } else {
            scrollMessagesContainerToBottom();
        }
        console.info('[MaiChat] restored', list.length, 'messages');
    }

    function setPanelOpen(open) {
        var panel = $('mai-chat-panel');
        var btn = $('mai-chat-launcher');
        if (!panel || !btn) return;

        if (!open) {
            var area = $('mai-chat-scroll-area');
            if (area) savedMessagesScrollTop = area.scrollTop;
        }

        panel.hidden = false;
        if (open) {
            panel.classList.add('is-open');
            btn.setAttribute('aria-expanded', 'true');
            pendingUnread = 0;
            lastBadgeValue = 0;
            if (window.ChestAiHub && typeof window.ChestAiHub.resetHubBadge === 'function') {
                window.ChestAiHub.resetHubBadge();
            }
            updateBadge();
        } else {
            panel.classList.remove('is-open');
            btn.setAttribute('aria-expanded', 'false');
        }
    }

    function openFloatingAssistantAndRefresh() {
        var snapUnread = pendingUnread > 0;
        var hubSnap =
            window.ChestAiHub &&
            typeof window.ChestAiHub.getHubUnread === 'function' &&
            window.ChestAiHub.getHubUnread() > 0;
        var preferBottom = snapUnread || hubSnap;

        var panel = $('mai-chat-panel');
        if (panel && panel.classList.contains('mai-chat-panel--minimized')) {
            panel.classList.remove('mai-chat-panel--minimized');
        }

        setPanelOpen(true);
        refreshFloatingChatFromServer(function () {
            if (!preferBottom) {
                restoreMessagesScroll();
            } else {
                scrollMessagesContainerToBottom();
                pulseHighlightUnread();
            }
        }, { scrollBottom: preferBottom });
    }

    function bindServiceWorkerOpenChat() {
        if (!('serviceWorker' in navigator)) return;
        navigator.serviceWorker.addEventListener('message', function (event) {
            var d = event.data;
            if (!d || d.type !== 'mai-open-chat') return;
            openFloatingAssistantAndRefresh();
        });
    }

    function updateBadge() {
        var b = $('mai-chat-badge');
        var launcher = $('mai-chat-launcher');
        if (pendingUnread > 0) {
            if (b) {
                if (pendingUnread > lastBadgeValue) {
                    b.classList.remove('mai-chat-badge--arrive');
                    void b.offsetWidth;
                    b.classList.add('mai-chat-badge--arrive');
                    setTimeout(function () {
                        b.classList.remove('mai-chat-badge--arrive');
                    }, 720);
                }
                lastBadgeValue = pendingUnread;
                b.hidden = false;
                b.textContent = pendingUnread > 99 ? '99+' : String(pendingUnread);
                b.setAttribute('aria-label', pendingUnread + ' unread messages');
            }
            if (launcher) launcher.classList.add('mai-chat-has-unread');
        } else {
            lastBadgeValue = 0;
            if (b) {
                b.hidden = true;
                b.removeAttribute('aria-label');
            }
            if (launcher) launcher.classList.remove('mai-chat-has-unread');
        }
    }

    function bumpUnreadIfClosed(opts) {
        opts = opts || {};
        var panel = $('mai-chat-panel');
        if (panel && panel.classList.contains('is-open')) return;
        pendingUnread++;
        if (!opts.silent) playIncomingUnreadPing();
        updateBadge();
    }

    function requestNotifyPermission() {
        if (!('Notification' in window)) return Promise.resolve('unsupported');
        if (Notification.permission === 'granted') return Promise.resolve('granted');
        if (Notification.permission === 'denied') return Promise.resolve('denied');
        return Notification.requestPermission();
    }

    function registerServiceWorker() {
        if (!('serviceWorker' in navigator)) return;
        navigator.serviceWorker
            .register('/sw.js')
            .then(function (reg) {
                console.info('[MaiChat] service worker registered', reg.scope);
            })
            .catch(function (e) {
                console.warn('[MaiChat] SW register failed', e);
            });
    }

    function syncTypingFromActivePolls() {
        var n = window.MedicalAiJobTracker.activePollCount();
        if (n > 0) {
            var jobs = window.MedicalAiChatStorage.loadPendingCtJobs();
            var last = jobs.length ? jobs[jobs.length - 1] : null;
            var msg =
                last && last.kind === 'xray'
                    ? 'Analyzing chest X-ray (CheXNet)…'
                    : 'Analyzing CT scan…';
            setTyping(true, msg);
        } else {
            setTyping(false);
        }
    }

    function finalizeCtFailure(jobId, message) {
        window.MedicalAiJobTracker.stopPolling(jobId);
        window.MedicalAiChatStorage.removePendingCtJob(jobId);

        if (window.MedicalAiChatStorage.findMessageForCtResult(jobId)) {
            console.info('[MaiChat] skip duplicate CT error UI', jobId);
            syncTypingFromActivePolls();
            return;
        }

        refreshFloatingChatFromServer(function () {
            window.MedicalAiChatStorage.markCtJobTerminal(jobId, 'failed');
            bumpUnreadIfClosed({ silent: true });
            notifyAnalysisFinished(jobId, false, message);
            syncTypingFromActivePolls();
            console.info('[MaiChat] CT job failed UI (server sync)', jobId);
        });
    }

    function finalizeCtSuccess(jobId, data) {
        window.MedicalAiJobTracker.stopPolling(jobId);
        window.MedicalAiChatStorage.removePendingCtJob(jobId);

        if (window.MedicalAiChatStorage.findMessageForCtResult(jobId)) {
            console.info('[MaiChat] skip duplicate CT result UI', jobId);
            syncTypingFromActivePolls();
            return;
        }

        refreshFloatingChatFromServer(function () {
            window.MedicalAiChatStorage.markCtJobTerminal(jobId, 'done');
            notifyAnalysisFinished(jobId, true, null);
            bumpUnreadIfClosed({ silent: true });
            syncTypingFromActivePolls();
            console.info('[MaiChat] CT job done UI (server sync)', jobId);
        });
    }

    function buildPollingOptions(jobId, imagingKind) {
        imagingKind = imagingKind || 'ct';
        var thinkingLabel =
            imagingKind === 'xray'
                ? 'Analyzing chest X-ray (CheXNet)…'
                : 'Analyzing CT scan…';
        return {
            thinkingLabel: thinkingLabel,
            onProcessing: function () {
                setTyping(true, thinkingLabel);
            },
            onDone: function (data) {
                console.info('[MaiChat] poll onDone', jobId);
                finalizeCtSuccess(jobId, data);
            },
            onFailed: function (err) {
                console.info('[MaiChat] poll onFailed', jobId, err);
                finalizeCtFailure(jobId, typeof err === 'string' ? err : err.message || 'Failed');
            }
        };
    }

    function startImagingJobPolling(jobId, fileLabel, imagingKind) {
        if (!jobId) return;
        fileLabel = fileLabel || 'scan';
        imagingKind = imagingKind || 'ct';

        requestNotifyPermission();
        registerServiceWorker();

        window.MedicalAiChatStorage.upsertPendingCtJob({
            jobId: jobId,
            fileLabel: fileLabel,
            kind: imagingKind
        });

        var panel = $('mai-chat-panel');
        if (panel && panel.classList.contains('mai-chat-panel--minimized')) {
            panel.classList.remove('mai-chat-panel--minimized');
        }

        setPanelOpen(true);

        setTimeout(function () {
            refreshFloatingChatFromServer(function () {}, { scrollBottom: true });
        }, 350);

        var opts = buildPollingOptions(jobId, imagingKind);
        setTyping(true, opts.thinkingLabel);

        window.MedicalAiJobTracker.startCtPolling(jobId, fileLabel, opts);
        console.info('[MaiChat] startImagingJobPolling', imagingKind, jobId);
    }

    function resumePendingCtJobs() {
        var jobs = window.MedicalAiChatStorage.loadPendingCtJobs();
        if (!jobs.length) return;

        console.info('[MaiChat] resume pending CT jobs', jobs.length);
        jobs.forEach(function (job) {
            var jid = job.jobId;
            var label = job.fileLabel || 'scan';

            if (window.MedicalAiChatStorage.findMessageForCtResult(jid)) {
                console.info('[MaiChat] pending job already has result message — cleaning up', jid);
                window.MedicalAiChatStorage.removePendingCtJob(jid);
                return;
            }

            window.MedicalAiJobTracker.startCtPolling(jid, label, buildPollingOptions(jid, job.kind || 'ct'));
        });

        syncTypingFromActivePolls();
    }

    function bindUi() {
        var launcher = $('mai-chat-launcher');
        var closeBtn = $('mai-chat-close');
        var minBtn = $('mai-chat-minimize');
        var wideBtn = $('mai-chat-width-toggle');
        var panel = $('mai-chat-panel');
        var soundOn = $('mai-chat-sound-on');
        var root = $('medical-ai-chat-root');

        if (launcher) {
            function tapFeedback() {
                launcher.classList.remove('mai-chat-launcher--tap');
                void launcher.offsetWidth;
                launcher.classList.add('mai-chat-launcher--tap');
                window.clearTimeout(launcher._maiTapT);
                launcher._maiTapT = window.setTimeout(function () {
                    launcher.classList.remove('mai-chat-launcher--tap');
                }, 440);
            }
            launcher.addEventListener('pointerdown', function () {
                tapFeedback();
            });
            launcher.addEventListener('click', function () {
                requestNotifyPermission();
                openFloatingAssistantAndRefresh();
            });
        }
        if (closeBtn) {
            closeBtn.addEventListener('click', function () {
                setPanelOpen(false);
            });
        }
        if (minBtn && panel) {
            minBtn.addEventListener('click', function (ev) {
                ev.stopPropagation();
                if (!panel.classList.contains('is-open')) return;
                panel.classList.toggle('mai-chat-panel--minimized');
            });
        }
        if (wideBtn && panel) {
            wideBtn.addEventListener('click', function (ev) {
                ev.stopPropagation();
                var on = panel.classList.toggle('mai-chat-panel--wide');
                wideBtn.setAttribute('aria-pressed', on ? 'true' : 'false');
            });
        }

        if (soundOn) {
            try {
                soundOn.checked = localStorage.getItem('maiQuiet') !== '1';
            } catch (_) {
                soundOn.checked = true;
            }
            soundOn.addEventListener('change', function () {
                try {
                    localStorage.setItem('maiQuiet', soundOn.checked ? '0' : '1');
                } catch (_) {}
            });
        }

        if (root) {
            root.addEventListener('click', function (ev) {
                var t = ev.target && ev.target.closest ? ev.target.closest('[data-mai-nav]') : null;
                if (!t || !root.contains(t)) return;
                var href = t.getAttribute('data-mai-nav');
                if (href) window.location.href = href;
            });
        }

        document.addEventListener('mai-hub-badge', function () {
            bumpUnreadIfClosed();
        });

        window.addEventListener('mai-job-update', function (ev) {
            var dto = ev.detail;
            if (!dto || !dto.jobId) return;
            if (dto.kind !== 'ct' && dto.kind !== 'xray') return;
            if (dto.status === 'done') finalizeCtSuccess(dto.jobId, { result: dto.result });
            else if (dto.status === 'failed') finalizeCtFailure(dto.jobId, dto.error || 'Failed');
        });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && panel && panel.classList.contains('is-open')) {
                setPanelOpen(false);
            }
        });
    }

    function init() {
        if (!$('medical-ai-chat-root')) return;

        bindUi();
        bindServiceWorkerOpenChat();
        registerServiceWorker();

        setSkeletonVisible(true);
        refreshFloatingChatFromServer(function () {
            setSkeletonVisible(false);
            resumePendingCtJobs();
            console.info('[MaiChat] initialized');
        }, { scrollBottom: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

        window.MedicalAiAssistantChat = {
            startCtJobPolling: function (jobId, fileLabel) {
                startImagingJobPolling(jobId, fileLabel, 'ct');
            },
            startXRayJobPolling: function (jobId, fileLabel) {
                startImagingJobPolling(jobId, fileLabel, 'xray');
            },
            requestNotifyPermission: requestNotifyPermission,
            openChat: function () {
                openFloatingAssistantAndRefresh();
            },
            playSoftPing: playSoftPing,
            playIncomingUnreadPing: playIncomingUnreadPing
        };
})();
