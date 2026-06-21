/* Service worker: notifications + focus app when user taps notification */
self.addEventListener('install', function (event) {
    self.skipWaiting();
});

self.addEventListener('activate', function (event) {
    event.waitUntil(self.clients.claim());
});

self.addEventListener('notificationclick', function (event) {
    event.notification.close();
    event.waitUntil(
        self.clients.matchAll({ type: 'window', includeUncontrolled: true }).then(function (clientList) {
            var scope = self.registration.scope || '';
            var payload = { type: 'mai-open-chat' };
            for (var i = 0; i < clientList.length; i++) {
                var c = clientList[i];
                try {
                    c.postMessage(payload);
                } catch (e) {
                    /* ignore */
                }
                if (c.focus) {
                    return c.focus();
                }
            }
            if (self.clients.openWindow) {
                return self.clients.openWindow(scope || '/');
            }
        })
    );
});

self.addEventListener('message', function (event) {
    var d = event.data;
    if (!d || d.type !== 'mai-show-notification') return;
    event.waitUntil(
        self.registration.showNotification(d.title || 'ChestAI — Analysis ready', {
            body: d.body || 'Your scan finished. Open the app to review.',
            icon: d.icon || '/images/chest-icon.png',
            tag: d.tag || 'mai-ct'
        })
    );
});
