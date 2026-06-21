/**
 * SignalR: realtime background job completion (CT / chest X-ray + async assistant). Dispatches mai-job-update; toast + badge.
 */
(function () {
    'use strict';

    var hubExtraUnread = 0;

    function hubPath() {
        return typeof window.__maiAssistantHubUrl === 'string' && window.__maiAssistantHubUrl.length > 0
            ? window.__maiAssistantHubUrl
            : '/hubs/assistant';
    }

    function escapeHtml(s) {
        var d = document.createElement('div');
        d.textContent = s == null ? '' : String(s);
        return d.innerHTML;
    }

    function openAssistantFromNotify() {
        try {
            window.focus();
        } catch (_) {
            /* ignore */
        }
        try {
            if (
                window.MedicalAiAssistantChat &&
                typeof window.MedicalAiAssistantChat.openChat === 'function'
            ) {
                window.MedicalAiAssistantChat.openChat();
            }
        } catch (_) {
            /* ignore */
        }
    }

    function showToast(title, body, kind) {
        try {
            var el = document.getElementById('mai-hub-toast');
            if (!el) {
                el = document.createElement('div');
                el.id = 'mai-hub-toast';
                el.className = 'mai-hub-toast';
                document.body.appendChild(el);
            }
            el.className =
                'mai-hub-toast mai-hub-toast--visible mai-hub-toast--' + (kind === 'err' ? 'err' : kind === 'ok' ? 'ok' : 'info');
            el.setAttribute('role', 'button');
            el.setAttribute('tabindex', '0');
            el.setAttribute('title', 'Open ChestAI chat');
            el.innerHTML =
                '<strong>' +
                escapeHtml(title) +
                '</strong><div class="mai-hub-toast-body">' +
                escapeHtml(body || '') +
                '</div>';
            el.onclick = function () {
                clearTimeout(showToast._t);
                el.classList.remove('mai-hub-toast--visible');
                openAssistantFromNotify();
            };
            el.onkeydown = function (kev) {
                if (kev.key === 'Enter' || kev.key === ' ') {
                    kev.preventDefault();
                    el.onclick();
                }
            };
            clearTimeout(showToast._t);
            showToast._t = setTimeout(function () {
                el.classList.remove('mai-hub-toast--visible');
            }, 5500);
        } catch (_) {
            /* ignore */
        }
    }

    function bumpHubBadge(delta) {
        hubExtraUnread += typeof delta === 'number' ? delta : 1;
        try {
            window.dispatchEvent(new CustomEvent('mai-hub-badge', { detail: { delta: delta, totalHub: hubExtraUnread } }));
        } catch (_) {
            /* ignore */
        }
    }

    window.ChestAiHub = {
        resetHubBadge: function () {
            hubExtraUnread = 0;
            try {
                window.dispatchEvent(new CustomEvent('mai-hub-badge-reset'));
            } catch (_) {
                /* ignore */
            }
        },
        getHubUnread: function () {
            return hubExtraUnread;
        }
    };

    function start() {
        if (typeof signalR === 'undefined') {
            console.warn('[ChestAiHub] @microsoft/signalr not loaded');
            return;
        }

        var connection = new signalR.HubConnectionBuilder()
            .withUrl(hubPath(), {
                transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling
            })
            .withAutomaticReconnect([0, 2000, 5000, 10000, 20000])
            .configureLogging(signalR.LogLevel.Warning)
            .build();

        connection.on('jobUpdate', function (dto) {
            console.info('[ChestAiHub] jobUpdate', dto && dto.jobId, dto && dto.status);
            try {
                window.dispatchEvent(new CustomEvent('mai-job-update', { detail: dto }));
            } catch (_) {
                /* ignore */
            }

            if (dto && (dto.status === 'done' || dto.status === 'failed')) {
                bumpHubBadge(1);
                var title = dto.status === 'done' ? 'ChestAI — Ready' : 'ChestAI — Issue';
                var body =
                    dto.kind === 'assistant_chat'
                        ? dto.status === 'done'
                            ? 'Assistant reply is ready.'
                            : String(dto.error || 'Assistant job failed').slice(0, 180)
                        : dto.status === 'done'
                          ? 'Scan analysis finished.'
                          : String(dto.error || 'Analysis failed').slice(0, 180);
                showToast(title, body, dto.status === 'done' ? 'ok' : 'err');

                if (dto.kind === 'assistant_chat') {
                    try {
                        if (
                            window.MedicalAiAssistantChat &&
                            typeof window.MedicalAiAssistantChat.playSoftPing === 'function'
                        ) {
                            window.MedicalAiAssistantChat.playSoftPing();
                        }
                    } catch (_) {}
                }

                if ('Notification' in window && Notification.permission === 'granted') {
                    try {
                        var n = new Notification(title, { body: body, tag: 'mai-job-' + dto.jobId });
                        n.onclick = function () {
                            try {
                                n.close();
                            } catch (_) {
                                /* ignore */
                            }
                            openAssistantFromNotify();
                        };
                    } catch (_) {
                        /* ignore */
                    }
                }
            }
        });

        connection
            .start()
            .then(function () {
                console.info('[ChestAiHub] connected', hubPath());
            })
            .catch(function (err) {
                console.warn('[ChestAiHub] connect failed (polling still works)', err);
            });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
