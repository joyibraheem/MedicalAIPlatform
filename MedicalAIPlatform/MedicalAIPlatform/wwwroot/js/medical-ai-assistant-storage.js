/**
 * Persistent chat + CT job metadata (localStorage), scoped per signed-in user.
 */
(function () {
    'use strict';

    var LEGACY_MESSAGES = 'mai.chat.v1.messages';
    var LEGACY_PENDING_CT = 'mai.chat.v1.pendingCtJobs';
    var LEGACY_COMPLETED_CT = 'mai.chat.v1.completedCtJobs';

    var MAX_MESSAGES = 320;
    var MAX_COMPLETED_IDS = 80;
    var migratedFlagPrefix = 'mai.chat.v1.migratedLegacy.';

    function userScopeSuffix() {
        var id =
            typeof window.__maiClinicalUserId === 'string'
                ? window.__maiClinicalUserId.trim()
                : '';
        return id.length > 0 ? '.' + id : '';
    }

    function keyMessages() {
        return LEGACY_MESSAGES + userScopeSuffix();
    }

    function keyPendingCt() {
        return LEGACY_PENDING_CT + userScopeSuffix();
    }

    function keyCompletedCt() {
        return LEGACY_COMPLETED_CT + userScopeSuffix();
    }

    /** Copy pre-user-scope data into this user's bucket once (logout/login keeps same user id → stable keys). */
    function migrateLegacyIntoScopedOnce() {
        var suf = userScopeSuffix();
        if (!suf) return;

        var flagKey = migratedFlagPrefix + suf.replace(/^\./, '');
        try {
            if (localStorage.getItem(flagKey)) return;
        } catch (_) {
            return;
        }

        var pairs = [
            [LEGACY_MESSAGES, keyMessages()],
            [LEGACY_PENDING_CT, keyPendingCt()],
            [LEGACY_COMPLETED_CT, keyCompletedCt()]
        ];

        pairs.forEach(function (pair) {
            var legacyKey = pair[0];
            var scopedKey = pair[1];
            if (legacyKey === scopedKey) return;

            try {
                var scopedVal = localStorage.getItem(scopedKey);
                var scopedEmpty =
                    scopedVal === null ||
                    scopedVal === '' ||
                    scopedVal === '[]' ||
                    scopedVal === 'null';

                if (!scopedEmpty) return;

                var legacyVal = localStorage.getItem(legacyKey);
                if (legacyVal && legacyVal !== '[]' && legacyVal !== 'null') {
                    localStorage.setItem(scopedKey, legacyVal);
                    console.info('[MaiStorage] migrated legacy → user scope', scopedKey);
                }
            } catch (e) {
                console.warn('[MaiStorage] migrate failed', e);
            }
        });

        try {
            localStorage.setItem(flagKey, new Date().toISOString());
        } catch (_) { /* ignore */ }
    }

    migrateLegacyIntoScopedOnce();

    function safeParse(json, fallback) {
        try {
            return JSON.parse(json);
        } catch (_) {
            return fallback;
        }
    }

    function loadMessages() {
        var raw = localStorage.getItem(keyMessages());
        var arr = safeParse(raw, []);
        return Array.isArray(arr) ? arr : [];
    }

    function saveMessages(arr) {
        localStorage.setItem(keyMessages(), JSON.stringify(arr));
    }

    /** Replace message list (e.g. after syncing from server). */
    function setMessagesFromApi(entries) {
        var list = Array.isArray(entries) ? entries : [];
        saveMessages(list);
        return list;
    }

    function loadCompletedCtJobIds() {
        var raw = localStorage.getItem(keyCompletedCt());
        var arr = safeParse(raw, []);
        return Array.isArray(arr) ? arr : [];
    }

    function saveCompletedCtJobIds(ids) {
        var trimmed = ids.slice(-MAX_COMPLETED_IDS);
        localStorage.setItem(keyCompletedCt(), JSON.stringify(trimmed));
    }

    function appendMessage(entry) {
        var list = loadMessages();
        list.push(entry);
        if (list.length > MAX_MESSAGES) list = list.slice(-MAX_MESSAGES);
        saveMessages(list);
        return entry;
    }

    function loadPendingCtJobs() {
        var raw = localStorage.getItem(keyPendingCt());
        var arr = safeParse(raw, []);
        return Array.isArray(arr) ? arr : [];
    }

    function savePendingCtJobs(jobs) {
        localStorage.setItem(keyPendingCt(), JSON.stringify(jobs));
    }

    function upsertPendingCtJob(job) {
        var jobs = loadPendingCtJobs().filter(function (j) {
            return j.jobId !== job.jobId;
        });
        jobs.push({
            jobId: job.jobId,
            fileLabel: job.fileLabel || 'scan',
            kind: job.kind || 'ct',
            addedAt: job.addedAt || new Date().toISOString()
        });
        savePendingCtJobs(jobs);
        console.info('[MaiStorage] pending CT job saved', job.jobId);
    }

    function removePendingCtJob(jobId) {
        var jobs = loadPendingCtJobs().filter(function (j) {
            return j.jobId !== jobId;
        });
        savePendingCtJobs(jobs);
        console.info('[MaiStorage] pending CT job removed', jobId);
    }

    function markCtJobTerminal(jobId, outcome) {
        var ids = loadCompletedCtJobIds();
        if (ids.indexOf(jobId) === -1) {
            ids.push(jobId + ':' + outcome + ':' + Date.now());
            saveCompletedCtJobIds(ids);
        }
        console.info('[MaiStorage] CT job marked terminal', jobId, outcome);
    }

    function hasCtTerminalRecord(jobId) {
        return loadCompletedCtJobIds().some(function (id) {
            return String(id).indexOf(jobId + ':') === 0;
        });
    }

    function findMessageForCtResult(jobId) {
        return loadMessages().some(function (m) {
            var jid = m.jobId != null ? String(m.jobId) : '';
            return (
                jid === String(jobId) &&
                (m.kind === 'ct-result' ||
                    m.kind === 'ct-error' ||
                    m.kind === 'xray-result' ||
                    m.kind === 'xray-error')
            );
        });
    }

    window.MedicalAiChatStorage = {
        appendMessage: appendMessage,
        loadMessages: loadMessages,
        setMessagesFromApi: setMessagesFromApi,
        upsertPendingCtJob: upsertPendingCtJob,
        removePendingCtJob: removePendingCtJob,
        loadPendingCtJobs: loadPendingCtJobs,
        markCtJobTerminal: markCtJobTerminal,
        hasCtTerminalRecord: hasCtTerminalRecord,
        findMessageForCtResult: findMessageForCtResult,
        /** Current storage scope (for debugging). */
        getScopeSuffix: userScopeSuffix,
        /** Dev helper — clears this user's assistant data only. */
        clearAll: function () {
            localStorage.removeItem(keyMessages());
            localStorage.removeItem(keyPendingCt());
            localStorage.removeItem(keyCompletedCt());
            try {
                localStorage.removeItem(migratedFlagPrefix + userScopeSuffix().replace(/^\./, ''));
            } catch (_) { /* ignore */ }
            console.info('[MaiStorage] cleared user scope');
        }
    };
})();
