// MedAI Chat — UI + localStorage history (per user: window.__maiClinicalUserId from _DoctorLayout)
(function () {
    let chatHistory = [];
    let uploadedFiles = [];
    let isBusy = false;

    const LEGACY_KEY = 'mai.medai.v1.history';
    const MAX_MESSAGES = 200;

    const WELCOME_TEXT =
        "I'm your medical AI assistant. I can explain thoracic pathologies, lung cancer types, and clinical findings from X-ray, text, and CT. Run analysis on the Analytics page first for context.";

    function userSuffix() {
        const id =
            typeof window.__maiClinicalUserId === 'string'
                ? window.__maiClinicalUserId.trim()
                : '';
        return id.length > 0 ? '.' + id : '';
    }

    function storageKey() {
        return LEGACY_KEY + userSuffix();
    }

    function migrateLegacyOnce() {
        const scoped = storageKey();
        if (scoped === LEGACY_KEY) return;

        try {
            const flag = 'mai.medai.v1.migrated' + userSuffix();
            if (localStorage.getItem(flag)) return;

            const cur = localStorage.getItem(scoped);
            const empty = !cur || cur === '[]' || cur === 'null';
            if (!empty) {
                localStorage.setItem(flag, '1');
                return;
            }

            const legacy = localStorage.getItem(LEGACY_KEY);
            if (legacy && legacy !== '[]' && legacy !== 'null') {
                localStorage.setItem(scoped, legacy);
                console.info('[MedAI] migrated chat history to user scope');
            }
            localStorage.setItem(flag, '1');
        } catch (e) {
            console.warn('[MedAI] migrate failed', e);
        }
    }

    function normalizeMsg(m) {
        if (!m || typeof m !== 'object') return null;
        let role = (m.role || m.Role || 'user').toString().toLowerCase();
        if (role !== 'user' && role !== 'assistant') return null;
        const content = (m.content ?? m.Content ?? '').toString();
        return { role: role, content: content };
    }

    /** Map API rows (includes system) into MedAI bubbles (system → assistant label). */
    function normalizeServerMsg(m) {
        if (!m || typeof m !== 'object') return null;
        let role = (m.role || m.Role || 'assistant').toString().toLowerCase();
        if (role === 'system') role = 'assistant';
        if (role !== 'user' && role !== 'assistant') return null;
        const content = (m.content ?? m.Content ?? '').toString();
        if (!content.length) return null;
        return { role: role, content: content };
    }

    async function refreshMedAiHistoryFromServer() {
        const url =
            typeof window.__maiChatMessagesUrl === 'string' && window.__maiChatMessagesUrl.trim().length > 0
                ? window.__maiChatMessagesUrl.trim()
                : '/api/assistant/chat/messages';
        try {
            const response = await fetch(url + '?take=200', {
                credentials: 'same-origin',
                headers: { Accept: 'application/json' }
            });
            if (!response.ok) throw new Error('HTTP ' + response.status);
            const data = await response.json();
            const rows = Array.isArray(data.messages) ? data.messages : [];
            const out = [];
            for (let i = 0; i < rows.length; i++) {
                const n = normalizeServerMsg(rows[i]);
                if (n) out.push(n);
            }
            chatHistory = out.slice(-MAX_MESSAGES);
            saveHistoryToStorage();
        } catch (e) {
            console.warn('[MedAI] server history failed — using local cache', e);
            chatHistory = loadHistoryFromStorage();
        }
    }

    function loadHistoryFromStorage() {
        migrateLegacyOnce();
        try {
            const raw = localStorage.getItem(storageKey());
            const parsed = raw ? JSON.parse(raw) : [];
            if (!Array.isArray(parsed)) return [];

            const out = [];
            for (let i = 0; i < parsed.length; i++) {
                const n = normalizeMsg(parsed[i]);
                if (n && n.content.length > 0) out.push(n);
            }
            return out.slice(-MAX_MESSAGES);
        } catch (e) {
            console.warn('[MedAI] load history failed', e);
            return [];
        }
    }

    function saveHistoryToStorage() {
        try {
            localStorage.setItem(
                storageKey(),
                JSON.stringify(chatHistory.slice(-MAX_MESSAGES))
            );
            console.info('[MedAI] history saved', chatHistory.length, 'messages');
        } catch (e) {
            console.warn('[MedAI] save history failed', e);
        }
    }

    function hideQuickActionsIfNeeded() {
        const actionButtons = document.querySelector('.medai-action-buttons');
        if (actionButtons && chatHistory.length > 0) {
            actionButtons.style.display = 'none';
        }
    }

    /** DOM only — does not modify chatHistory */
    function addMessage(role, content, isTyping) {
        const messagesContainer = document.getElementById('chat-messages');
        if (!messagesContainer) return;

        const actionButtons = document.querySelector('.medai-action-buttons');
        if (actionButtons && chatHistory.length > 0) {
            actionButtons.style.display = 'none';
        }

        const messageWrapper = document.createElement('div');
        messageWrapper.className = 'medai-message-wrapper ' + role + '-message';

        const senderLabel = document.createElement('div');
        senderLabel.className = 'medai-sender-label';
        senderLabel.textContent = role === 'user' ? 'You' : 'MedAI';

        const messageBubble = document.createElement('div');
        messageBubble.className = 'medai-message-bubble ' + role + '-bubble';

        if (isTyping) {
            messageBubble.innerHTML =
                '<div class="typing-indicator"><span></span><span></span><span></span></div>';
        } else {
            messageBubble.innerHTML = processMessage(content);
        }

        messageWrapper.appendChild(senderLabel);
        messageWrapper.appendChild(messageBubble);

        if (role === 'user') {
            const avatar = document.createElement('img');
            avatar.src = '/images/doctor-avatar.png';
            avatar.alt = 'You';
            avatar.className = 'user-avatar';
            avatar.style.display = 'none';
            avatar.onerror = function () {
                this.style.display = 'none';
            };
            messageWrapper.appendChild(avatar);
        }

        messagesContainer.appendChild(messageWrapper);
        scrollToBottom();
    }

    function renderAllMessages() {
        const mc = document.getElementById('chat-messages');
        if (!mc) return;
        mc.innerHTML = '';
        hideQuickActionsIfNeeded();
        chatHistory.forEach(function (msg) {
            addMessage(msg.role, msg.content, false);
        });
        scrollToBottom();
    }

    async function initChat() {
        await refreshMedAiHistoryFromServer();

        chatHistory = chatHistory.length ? chatHistory : loadHistoryFromStorage();

        const chatInput = document.getElementById('chat-input');
        const sendBtn = document.getElementById('send-btn');
        const fileInput = document.getElementById('file-upload');
        const actionButtons = document.querySelectorAll('.medai-action-btn');

        const messagesContainer = document.getElementById('chat-messages');
        if (messagesContainer) {
            messagesContainer.innerHTML = '';
        }

        if (chatHistory.length > 0) {
            renderAllMessages();
        } else {
            addMessage('assistant', WELCOME_TEXT, false);
        }

        if (chatInput) {
            chatInput.addEventListener('input', updateSendButton);
            chatInput.addEventListener('keydown', function (e) {
                if (e.key === 'Enter' && !e.shiftKey && !isBusy) {
                    e.preventDefault();
                    sendMessage();
                }
            });
        }

        if (sendBtn) {
            sendBtn.addEventListener('click', sendMessage);
        }

        if (fileInput) {
            fileInput.addEventListener('change', handleFileUpload);
        }

        actionButtons.forEach(function (btn) {
            btn.addEventListener('click', function () {
                const text = this.textContent.trim();
                if (chatInput) {
                    chatInput.value = text;
                    updateSendButton();
                    chatInput.focus();
                }
            });
        });

        scrollToBottom();
        console.info('[MedAI] init; restored', chatHistory.length, 'messages; scope=', userSuffix() || '(legacy)');
    }

    function updateSendButton() {
        const chatInput = document.getElementById('chat-input');
        const sendBtn = document.getElementById('send-btn');

        if (!chatInput || !sendBtn) return;

        const hasInput = chatInput.value.trim().length > 0 || uploadedFiles.length > 0;

        if (hasInput && !isBusy) {
            sendBtn.classList.add('active');
            sendBtn.disabled = false;
        } else {
            sendBtn.classList.remove('active');
            sendBtn.disabled = true;
        }
    }

    function processMessage(content) {
        content = content.replace(
            /(\d+mm|\d+%|\d+\.\d+%|\d+mm\s+\w+|\d+%\s+\w+)/gi,
            '<strong>$1</strong>'
        );
        content = content.replace(/(\/MedicalReport\/Details\/[0-9a-fA-F-]{36})/gi, function (_, path) {
            return (
                '<a href="' +
                path +
                '" target="_blank" rel="noopener noreferrer" class="medai-report-link">Open medical report</a>'
            );
        });
        content = content.replace(/\n/g, '<br/>');
        return content;
    }

    function removeTypingIndicator() {
        const messagesContainer = document.getElementById('chat-messages');
        if (!messagesContainer) return;
        const lastMessage = messagesContainer.lastElementChild;
        if (lastMessage && lastMessage.querySelector('.typing-indicator')) {
            lastMessage.remove();
        }
    }

    function enqueueAssistantUrl() {
        const u =
            typeof window.__maiAssistantEnqueueUrl === 'string' ? window.__maiAssistantEnqueueUrl.trim() : '';
        return u.length > 0 ? u : '/api/assistant/chat/enqueue';
    }

    async function postEnqueueAssistant(bodyObj) {
        const opts = {
            method: 'POST',
            credentials: 'same-origin',
            cache: 'no-store',
            headers: {
                'Content-Type': 'application/json',
                Accept: 'application/json',
                RequestVerificationToken:
                    document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
            },
            body: JSON.stringify(bodyObj)
        };

        try {
            return await fetch(enqueueAssistantUrl(), opts);
        } catch (firstErr) {
            console.warn('[MedAI] fetch retry after', firstErr && firstErr.message);
            await new Promise(function (r) {
                setTimeout(r, 450);
            });
            return await fetch(enqueueAssistantUrl(), opts);
        }
    }

    function jobStatusUrl(jobId) {
        let p =
            typeof window.__maiJobStatusUrlPrefix === 'string' && window.__maiJobStatusUrlPrefix.length > 0
                ? window.__maiJobStatusUrlPrefix
                : '/api/job/status/';
        if (!p.endsWith('/')) p += '/';
        return p + encodeURIComponent(jobId);
    }

    function waitForAssistantJob(jobId) {
        const deadline = Date.now() + 360000;
        const idStr = String(jobId);
        return new Promise(function (resolve, reject) {
            let pollTimer = null;
            function cleanup() {
                window.removeEventListener('mai-job-update', onHub);
                if (pollTimer) clearInterval(pollTimer);
            }
            function onHub(ev) {
                const d = ev.detail;
                if (!d || String(d.jobId) !== idStr) return;
                if (d.kind !== 'assistant_chat') return;
                if (d.status === 'done') {
                    cleanup();
                    resolve();
                } else if (d.status === 'failed') {
                    cleanup();
                    reject(new Error(d.error || 'Assistant job failed'));
                }
            }
            window.addEventListener('mai-job-update', onHub);

            pollTimer = setInterval(function () {
                if (Date.now() > deadline) {
                    cleanup();
                    reject(new Error('Assistant reply timed out.'));
                    return;
                }
                fetch(jobStatusUrl(jobId), {
                    credentials: 'same-origin',
                    headers: { Accept: 'application/json' }
                })
                    .then(function (r) {
                        if (!r.ok) return null;
                        return r.json();
                    })
                    .then(function (data) {
                        if (!data) return;
                        if (data.status === 'done') {
                            cleanup();
                            resolve();
                        } else if (data.status === 'failed') {
                            cleanup();
                            reject(new Error(data.error || 'Failed'));
                        }
                    })
                    .catch(function () {
                        /* transient */
                    });
            }, 1300);
        });
    }

    async function sendMessage() {
        const chatInput = document.getElementById('chat-input');
        if (!chatInput || isBusy) return;

        const message = chatInput.value.trim();
        if (!message && uploadedFiles.length === 0) return;

        const fullMessage =
            message +
            (uploadedFiles.length > 0
                ? '\n\n[Attached ' +
                  uploadedFiles.length +
                  ' file(s): ' +
                  uploadedFiles.map(function (f) {
                      return f.name;
                  }).join(', ') +
                  ']'
                : '');

        const priorHistory = chatHistory.slice();

        hideQuickActionsIfNeeded();
        addMessage('user', fullMessage, false);

        chatInput.value = '';
        uploadedFiles = [];
        updateFilePreview();
        updateSendButton();
        isBusy = true;
        updateSendButton();

        addMessage('assistant', '', true);

        try {
            const response = await postEnqueueAssistant({
                message: message,
                history: priorHistory,
                patientContextId:
                    typeof window.__maiPatientContextId === 'number' && window.__maiPatientContextId > 0
                        ? window.__maiPatientContextId
                        : typeof window.__maiPatientContextId === 'string' &&
                            window.__maiPatientContextId.trim().match(/^\d+$/)
                          ? parseInt(window.__maiPatientContextId.trim(), 10)
                          : null
            });

            const ct = (response.headers.get('content-type') || '').toLowerCase();
            const isJson = ct.includes('application/json');

            if (!isJson) {
                removeTypingIndicator();
                throw new Error(
                    'Session may have expired or the server returned an unexpected response. Refresh the page and sign in again.'
                );
            }

            const envelope = await response.json();

            if (!response.ok) {
                removeTypingIndicator();
                throw new Error(envelope.error || 'HTTP error ' + response.status);
            }

            const jobId = envelope.jobId;
            if (!jobId) {
                removeTypingIndicator();
                throw new Error('Server did not return a job id.');
            }

            await waitForAssistantJob(jobId);

            removeTypingIndicator();
            await refreshMedAiHistoryFromServer();
            renderAllMessages();
            saveHistoryToStorage();
        } catch (error) {
            removeTypingIndicator();
            let errText = error && error.message ? error.message : 'Unknown error';
            if (error && error.name === 'AbortError') {
                errText =
                    'Request was interrupted (you switched tabs or navigated away). Send your message again.';
            } else if (errText === 'Failed to fetch' || (error && error.name === 'TypeError')) {
                errText =
                    'Could not reach the server. Refresh the page, wait a moment, and try again.';
            }
            chatHistory = priorHistory.concat([
                { role: 'user', content: fullMessage },
                { role: 'assistant', content: 'Error: ' + errText }
            ]);
            renderAllMessages();
            saveHistoryToStorage();
        } finally {
            isBusy = false;
            updateSendButton();
            scrollToBottom();
        }
    }

    function handleFileUpload(e) {
        const files = Array.from(e.target.files);
        files.forEach(function (file) {
            if (file.size > 10 * 1024 * 1024) {
                alert(file.name + ' is too large (max 10MB)');
                return;
            }
            uploadedFiles.push(file);
        });

        updateFilePreview();
        updateSendButton();
        e.target.value = '';
    }

    function updateFilePreview() {
        const previewContainer = document.querySelector('.medai-uploads-preview');
        if (!previewContainer) return;

        previewContainer.innerHTML = '';

        uploadedFiles.forEach(function (file, index) {
            const item = document.createElement('div');
            item.className = 'medai-upload-item';

            if (file.type.startsWith('image/')) {
                const reader = new FileReader();
                reader.onload = function (ev) {
                    const img = document.createElement('img');
                    img.src = ev.target.result;
                    img.className = 'upload-preview-img';
                    img.alt = file.name;
                    item.insertBefore(img, item.firstChild);
                };
                reader.readAsDataURL(file);
            } else {
                const icon = document.createElement('div');
                icon.className = 'upload-file-icon';
                icon.innerHTML =
                    '<svg width="24" height="24" viewBox="0 0 24 24" fill="none"><path d="M14 2H6C4.9 2 4 2.9 4 4V20C4 21.1 4.9 22 6 22H18C19.1 22 20 21.1 20 20V8L14 2Z" stroke="#374151" stroke-width="2"/><path d="M14 2V8H20" stroke="#374151" stroke-width="2"/></svg>';
                item.appendChild(icon);
            }

            const name = document.createElement('span');
            name.className = 'upload-file-name';
            name.textContent = file.name;
            item.appendChild(name);

            const removeBtn = document.createElement('button');
            removeBtn.className = 'upload-remove-btn';
            removeBtn.innerHTML =
                '<svg width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M12 4L4 12M4 4L12 12" stroke="#374151" stroke-width="2" stroke-linecap="round"/></svg>';
            removeBtn.onclick = function () {
                uploadedFiles.splice(index, 1);
                updateFilePreview();
                updateSendButton();
            };
            item.appendChild(removeBtn);

            previewContainer.appendChild(item);
        });
    }

    function scrollToBottom() {
        const messagesContainer = document.getElementById('chat-messages');
        if (messagesContainer) {
            setTimeout(function () {
                messagesContainer.scrollTop = messagesContainer.scrollHeight;
            }, 100);
        }
    }

    function boot() {
        initChat().catch(function (e) {
            console.warn('[MedAI] init failed', e);
        });
    }

    window.addEventListener('pageshow', function (ev) {
        if (ev.persisted) {
            isBusy = false;
            removeTypingIndicator();
            updateSendButton();
            refreshMedAiHistoryFromServer()
                .then(function () {
                    renderAllMessages();
                    saveHistoryToStorage();
                })
                .catch(function () {
                    /* ignore */
                });
        }
    });

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }

    window.MedAiChatPersist = {
        exportHistory: function () {
            return chatHistory.slice();
        },
        clearHistory: function () {
            chatHistory = [];
            try {
                localStorage.removeItem(storageKey());
            } catch (_) { /* ignore */ }
            const mc = document.getElementById('chat-messages');
            if (mc) mc.innerHTML = '';
            const ab = document.querySelector('.medai-action-buttons');
            if (ab) ab.style.display = '';
            addMessage('assistant', WELCOME_TEXT, false);
            console.info('[MedAI] history cleared');
        }
    };
})();
