// MedAI Chat JavaScript
(function() {
    let chatHistory = [];
    let uploadedFiles = [];
    let isBusy = false;

    // Initialize chat
    function initChat() {
        const chatInput = document.getElementById('chat-input');
        const sendBtn = document.getElementById('send-btn');
        const fileInput = document.getElementById('file-upload');
        const actionButtons = document.querySelectorAll('.medai-action-btn');

        // Add initial welcome message if no history
        if (chatHistory.length === 0) {
            addMessage('assistant', 'I\'m your medical AI assistant. I can explain thoracic pathologies, lung cancer types, and clinical findings from X-ray, text, and CT. Run analysis on the Analytics page first for context.');
        }

        // Input event listeners
        if (chatInput) {
            chatInput.addEventListener('input', updateSendButton);
            chatInput.addEventListener('keydown', function(e) {
                if (e.key === 'Enter' && !e.shiftKey && !isBusy) {
                    e.preventDefault();
                    sendMessage();
                }
            });
        }

        // Send button
        if (sendBtn) {
            sendBtn.addEventListener('click', sendMessage);
        }

        // File upload
        if (fileInput) {
            fileInput.addEventListener('change', handleFileUpload);
        }

        // Action buttons
        actionButtons.forEach(btn => {
            btn.addEventListener('click', function() {
                const text = this.textContent.trim();
                if (chatInput) {
                    chatInput.value = text;
                    updateSendButton();
                    chatInput.focus();
                }
            });
        });

        // Scroll to bottom on load
        scrollToBottom();
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

    function addMessage(role, content, isTyping = false) {
        const messagesContainer = document.getElementById('chat-messages');
        if (!messagesContainer) return;

        // Hide action buttons after first message
        const actionButtons = document.querySelector('.medai-action-buttons');
        if (actionButtons && chatHistory.length > 0) {
            actionButtons.style.display = 'none';
        }

        const messageWrapper = document.createElement('div');
        messageWrapper.className = `medai-message-wrapper ${role}-message`;

        const senderLabel = document.createElement('div');
        senderLabel.className = 'medai-sender-label';
        senderLabel.textContent = role === 'user' ? 'Dr. Smith' : 'MedAI';

        const messageBubble = document.createElement('div');
        messageBubble.className = `medai-message-bubble ${role}-bubble`;

        if (isTyping) {
            messageBubble.innerHTML = '<div class="typing-indicator"><span></span><span></span><span></span></div>';
        } else {
            // Process message content (highlight numbers, convert newlines)
            const processedContent = processMessage(content);
            messageBubble.innerHTML = processedContent;
        }

        messageWrapper.appendChild(senderLabel);
        messageWrapper.appendChild(messageBubble);

        // Add user avatar for user messages
        if (role === 'user') {
            const avatar = document.createElement('img');
            avatar.src = '/images/doctor-avatar.png'; // You can add a default avatar
            avatar.alt = 'Dr. Smith';
            avatar.className = 'user-avatar';
            avatar.style.display = 'none'; // Hide if no image available
            avatar.onerror = function() { this.style.display = 'none'; };
            messageWrapper.appendChild(avatar);
        }

        messagesContainer.appendChild(messageWrapper);
        scrollToBottom();
    }

    function processMessage(content) {
        // Highlight important medical terms and numbers
        content = content.replace(/(\d+mm|\d+%|\d+\.\d+%|\d+mm\s+\w+|\d+%\s+\w+)/gi, '<strong>$1</strong>');
        
        // Replace newlines with <br/>
        content = content.replace(/\n/g, '<br/>');
        
        return content;
    }

    async function sendMessage() {
        const chatInput = document.getElementById('chat-input');
        if (!chatInput || isBusy) return;

        const message = chatInput.value.trim();
        if (!message && uploadedFiles.length === 0) return;

        // Add user message
        const fullMessage = message + (uploadedFiles.length > 0 
            ? `\n\n[Attached ${uploadedFiles.length} file(s): ${uploadedFiles.map(f => f.name).join(', ')}]`
            : '');

        addMessage('user', fullMessage);
        chatHistory.push({ role: 'user', content: fullMessage });

        // Clear input and files
        chatInput.value = '';
        uploadedFiles = [];
        updateFilePreview();
        updateSendButton();
        isBusy = true;
        updateSendButton();

        // Show typing indicator
        addMessage('assistant', '', true);

        try {
            const response = await fetch('/AIAssistant/SendMessage', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
                },
                body: JSON.stringify({
                    message: message,
                    history: chatHistory
                })
            });

            if (!response.ok) {
                throw new Error(`HTTP error! status: ${response.status}`);
            }

            const data = await response.json();

            // Remove typing indicator
            const messagesContainer = document.getElementById('chat-messages');
            if (messagesContainer) {
                const lastMessage = messagesContainer.lastElementChild;
                if (lastMessage && lastMessage.querySelector('.typing-indicator')) {
                    lastMessage.remove();
                }
            }

            // Add assistant response
            addMessage('assistant', data.message);
            chatHistory = data.history || chatHistory;
        } catch (error) {
            // Remove typing indicator
            const messagesContainer = document.getElementById('chat-messages');
            if (messagesContainer) {
                const lastMessage = messagesContainer.lastElementChild;
                if (lastMessage && lastMessage.querySelector('.typing-indicator')) {
                    lastMessage.remove();
                }
            }

            addMessage('assistant', `Error: ${error.message}`);
        } finally {
            isBusy = false;
            updateSendButton();
            scrollToBottom();
        }
    }

    function handleFileUpload(e) {
        const files = Array.from(e.target.files);
        files.forEach(file => {
            if (file.size > 10 * 1024 * 1024) {
                alert(`${file.name} is too large (max 10MB)`);
                return;
            }

            uploadedFiles.push(file);
        });

        updateFilePreview();
        updateSendButton();
        e.target.value = ''; // Reset input
    }

    function updateFilePreview() {
        const previewContainer = document.querySelector('.medai-uploads-preview');
        if (!previewContainer) return;

        previewContainer.innerHTML = '';

        uploadedFiles.forEach((file, index) => {
            const item = document.createElement('div');
            item.className = 'medai-upload-item';

            if (file.type.startsWith('image/')) {
                const reader = new FileReader();
                reader.onload = function(e) {
                    const img = document.createElement('img');
                    img.src = e.target.result;
                    img.className = 'upload-preview-img';
                    img.alt = file.name;
                    item.insertBefore(img, item.firstChild);
                };
                reader.readAsDataURL(file);
            } else {
                const icon = document.createElement('div');
                icon.className = 'upload-file-icon';
                icon.innerHTML = '<svg width="24" height="24" viewBox="0 0 24 24" fill="none"><path d="M14 2H6C4.9 2 4 2.9 4 4V20C4 21.1 4.9 22 6 22H18C19.1 22 20 21.1 20 20V8L14 2Z" stroke="#374151" stroke-width="2"/><path d="M14 2V8H20" stroke="#374151" stroke-width="2"/></svg>';
                item.appendChild(icon);
            }

            const name = document.createElement('span');
            name.className = 'upload-file-name';
            name.textContent = file.name;
            item.appendChild(name);

            const removeBtn = document.createElement('button');
            removeBtn.className = 'upload-remove-btn';
            removeBtn.innerHTML = '<svg width="16" height="16" viewBox="0 0 16 16" fill="none"><path d="M12 4L4 12M4 4L12 12" stroke="#374151" stroke-width="2" stroke-linecap="round"/></svg>';
            removeBtn.onclick = () => {
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
            setTimeout(() => {
                messagesContainer.scrollTop = messagesContainer.scrollHeight;
            }, 100);
        }
    }

    // Initialize when DOM is ready
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initChat);
    } else {
        initChat();
    }
})();
