/**
 * CT job polling: single interval per jobId, resume after navigation, visibility flush.
 */
(function () {
    'use strict';

    var POLL_MS = 2600;
    var intervals = {};
    var pollFns = {};
    var visibilityBound = false;

    function log() {
        console.info.apply(console, ['[MaiJob]'].concat([].slice.call(arguments)));
    }

    function stopPolling(jobId) {
        if (intervals[jobId]) {
            clearInterval(intervals[jobId]);
            delete intervals[jobId];
        }
        delete pollFns[jobId];
        log('stopped polling', jobId);
    }

    function stopAllPolling() {
        Object.keys(intervals).forEach(stopPolling);
    }

    function bindVisibilityOnce() {
        if (visibilityBound) return;
        visibilityBound = true;
        document.addEventListener('visibilitychange', function () {
            if (document.visibilityState !== 'visible') return;
            log('tab visible — immediate poll for active jobs');
            flushPendingPolls();
        });
    }

    /**
     * @param {object} options
     * @param {function(object): void} [options.onProcessing]
     * @param {function(object): void} options.onDone
     * @param {function(Error|string): void} options.onFailed
     */
    function startCtPolling(jobId, fileLabel, options) {
        options = options || {};
        bindVisibilityOnce();

        if (intervals[jobId]) {
            clearInterval(intervals[jobId]);
            delete intervals[jobId];
            log('dedup: cleared existing interval', jobId);
        }

        function statusUrl(id) {
            var p =
                typeof window.__maiJobStatusUrlPrefix === 'string' &&
                window.__maiJobStatusUrlPrefix.length > 0
                    ? window.__maiJobStatusUrlPrefix
                    : '/api/job/status/';
            if (!p.endsWith('/')) p += '/';
            return p + encodeURIComponent(id);
        }

        function pollOnce() {
            fetch(statusUrl(jobId), {
                method: 'GET',
                credentials: 'same-origin',
                headers: { Accept: 'application/json' }
            })
                .then(function (r) {
                    if (r.status === 404) throw new Error('Job expired or not found.');
                    return r.json();
                })
                .then(function (data) {
                    log('status update', jobId, data && data.status);
                    var st = data && data.status;
                    if (st === 'processing' || st === 'queued') {
                        if (typeof options.onProcessing === 'function') options.onProcessing(data);
                        return;
                    }

                    stopPolling(jobId);

                    if (st === 'failed') {
                        var err = (data && data.error) || 'Analysis failed.';
                        if (typeof options.onFailed === 'function') options.onFailed(err);
                        return;
                    }

                    if (st === 'done') {
                        if (typeof options.onDone === 'function') options.onDone(data);
                        return;
                    }

                    if (typeof options.onFailed === 'function') {
                        options.onFailed('Unknown job status: ' + st);
                    }
                })
                .catch(function (e) {
                    log('poll error', jobId, e && e.message);
                    stopPolling(jobId);
                    if (typeof options.onFailed === 'function') {
                        options.onFailed(e.message || 'Status check failed.');
                    }
                });
        }

        pollFns[jobId] = pollOnce;
        log('polling started', jobId, fileLabel);
        pollOnce();
        intervals[jobId] = setInterval(pollOnce, POLL_MS);
    }

    function flushPendingPolls() {
        Object.keys(pollFns).forEach(function (jobId) {
            var fn = pollFns[jobId];
            if (typeof fn === 'function') fn();
        });
    }

    window.MedicalAiJobTracker = {
        startCtPolling: startCtPolling,
        stopPolling: stopPolling,
        stopAllPolling: stopAllPolling,
        flushPendingPolls: flushPendingPolls,
        activePollCount: function () {
            return Object.keys(intervals).length;
        }
    };
})();
