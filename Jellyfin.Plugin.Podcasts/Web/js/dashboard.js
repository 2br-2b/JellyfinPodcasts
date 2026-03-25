/**
 * Podcast Plugin Management Dashboard
 * Vue-like vanilla JS UI.
 */
(function () {
    'use strict';

    const API_BASE = '/podcasts';

    // ── Authentication helpers ─────────────────────────────────────────────────
    function getAuthHeader() {
        const token = window.ApiClient && window.ApiClient.accessToken
            ? window.ApiClient.accessToken()
            : null;
        return token ? { 'Authorization': `MediaBrowser Token="${token}"` } : {};
    }

    async function apiFetch(path, options = {}) {
        const resp = await fetch(`${API_BASE}${path}`, {
            ...options,
            headers: { 'Content-Type': 'application/json', ...getAuthHeader(), ...(options.headers || {}) }
        });
        if (!resp.ok) throw new Error(`API error ${resp.status}: ${await resp.text()}`);
        if (resp.status === 204) return null;
        return resp.json();
    }

    // ── Tab switching ──────────────────────────────────────────────────────────
    document.querySelectorAll('.tab-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
            document.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));
            btn.classList.add('active');
            document.getElementById(`tab-${btn.dataset.tab}`).classList.add('active');
        });
    });

    // ── Utility ────────────────────────────────────────────────────────────────
    function fmt(bytes) {
        if (bytes == null) return '?';
        if (bytes < 1024) return `${bytes} B`;
        if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KB`;
        if (bytes < 1024 ** 3) return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
        return `${(bytes / 1024 ** 3).toFixed(2)} GB`;
    }

    function escHtml(str) {
        return String(str ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // ══════════════════════════════════════════════════════════════════════════
    // SUBSCRIPTIONS TAB
    // ══════════════════════════════════════════════════════════════════════════
    async function loadSubscriptions() {
        const list = document.getElementById('subscriptions-list');
        list.innerHTML = '<p>Loading…</p>';
        try {
            const subs = await apiFetch('/management/subscriptions');
            if (!subs || subs.length === 0) {
                list.innerHTML = '<p>No subscriptions yet. Add a feed URL above.</p>';
                return;
            }
            list.innerHTML = subs.map(s => `
                <div class="card" data-podcast-id="${escHtml(s.id)}">
                    <strong>${escHtml(s.title || s.feed_url)}</strong>
                    ${s.unplayed_count ? `<span class="badge">${s.unplayed_count} unplayed</span>` : ''}
                    <div class="meta">${escHtml(s.author || '')} · Last: ${s.latest_episode_date ? new Date(s.latest_episode_date).toLocaleDateString() : 'unknown'}</div>
                    <div style="margin-top:.5em">
                        <button class="btn btn-secondary btn-refresh-feed" data-id="${escHtml(s.id)}">↻ Refresh</button>
                        <button class="btn btn-danger btn-unsubscribe" data-id="${escHtml(s.id)}" data-feed="${escHtml(s.feed_url)}">Unsubscribe</button>
                    </div>
                </div>
            `).join('');

            list.querySelectorAll('.btn-refresh-feed').forEach(btn => {
                btn.addEventListener('click', async () => {
                    btn.disabled = true; btn.textContent = '↻ Refreshing…';
                    await apiFetch(`/management/subscriptions/${btn.dataset.id}/refresh`, { method: 'POST' });
                    btn.textContent = '✓ Done'; setTimeout(() => { btn.disabled = false; btn.textContent = '↻ Refresh'; }, 2000);
                });
            });

            list.querySelectorAll('.btn-unsubscribe').forEach(btn => {
                btn.addEventListener('click', async () => {
                    if (!confirm(`Unsubscribe from ${btn.dataset.feed}?`)) return;
                    await apiFetch(`/sync/opa/subscriptions/${btn.dataset.id}`, { method: 'DELETE' });
                    loadSubscriptions();
                });
            });
        } catch (e) {
            list.innerHTML = `<p style="color:#f00">Error: ${escHtml(e.message)}</p>`;
        }
    }

    document.getElementById('btn-add-feed').addEventListener('click', async () => {
        const url = document.getElementById('new-feed-url').value.trim();
        if (!url) return;
        try {
            await apiFetch('/management/subscriptions', {
                method: 'POST',
                body: JSON.stringify({ feed_url: url })
            });
            document.getElementById('new-feed-url').value = '';
            loadSubscriptions();
        } catch (e) {
            alert(`Failed to subscribe: ${e.message}`);
        }
    });

    document.getElementById('btn-refresh-all').addEventListener('click', async () => {
        const btn = document.getElementById('btn-refresh-all');
        btn.disabled = true; btn.textContent = '↻ Refreshing…';
        try {
            await apiFetch('/management/refresh-all', { method: 'POST' });
            btn.textContent = '✓ Done';
        } catch (e) {
            alert(`Refresh failed: ${e.message}`);
            btn.textContent = '↻ Refresh All Feeds';
        }
        setTimeout(() => { btn.disabled = false; btn.textContent = '↻ Refresh All Feeds'; }, 3000);
    });

    // ══════════════════════════════════════════════════════════════════════════
    // CACHE TAB
    // ══════════════════════════════════════════════════════════════════════════
    async function loadCache() {
        const list = document.getElementById('cache-list');
        list.innerHTML = '<p>Loading…</p>';
        try {
            const data = await apiFetch('/management/cache');
            const usedBytes = data.used_bytes ?? 0;
            const quotaBytes = data.quota_bytes ?? 1;
            const pct = Math.min(100, (usedBytes / quotaBytes * 100)).toFixed(1);

            document.getElementById('disk-used-label').textContent = fmt(usedBytes);
            document.getElementById('disk-quota-label').textContent = `${fmt(quotaBytes)} quota`;
            document.getElementById('disk-bar-fill').style.width = `${pct}%`;

            if (!data.episodes || data.episodes.length === 0) {
                list.innerHTML = '<p>No cached episodes.</p>';
                return;
            }

            list.innerHTML = data.episodes.map(ep => `
                <div class="card">
                    <strong>${escHtml(ep.title)}</strong>
                    ${ep.is_pinned ? '<span class="badge">Pinned</span>' : '<span class="badge badge-cloud">LRU eligible</span>'}
                    <span class="meta"> · ${fmt(ep.cached_size_bytes)}</span>
                    <div class="meta">${escHtml(ep.podcast_title || '')}</div>
                    <div style="margin-top:.4em">
                        ${ep.is_pinned
                            ? `<button class="btn btn-secondary btn-unpin" data-id="${escHtml(ep.id)}">Unpin</button>`
                            : `<button class="btn btn-secondary btn-pin" data-id="${escHtml(ep.id)}">📌 Pin</button>`}
                    </div>
                </div>
            `).join('');

            list.querySelectorAll('.btn-pin').forEach(btn => {
                btn.addEventListener('click', async () => {
                    await apiFetch(`/management/cache/${btn.dataset.id}/pin`, { method: 'POST' });
                    loadCache();
                });
            });
            list.querySelectorAll('.btn-unpin').forEach(btn => {
                btn.addEventListener('click', async () => {
                    await apiFetch(`/management/cache/${btn.dataset.id}/unpin`, { method: 'POST' });
                    loadCache();
                });
            });
        } catch (e) {
            list.innerHTML = `<p style="color:#f00">Error: ${escHtml(e.message)}</p>`;
        }
    }

    document.getElementById('btn-evict').addEventListener('click', async () => {
        const btn = document.getElementById('btn-evict');
        btn.disabled = true; btn.textContent = 'Running…';
        try {
            await apiFetch('/management/cache/evict', { method: 'POST' });
            loadCache();
        } catch (e) {
            alert(`Eviction failed: ${e.message}`);
        }
        btn.disabled = false; btn.textContent = 'Run Eviction Now';
    });

    // ══════════════════════════════════════════════════════════════════════════
    // APP PASSWORDS TAB
    // ══════════════════════════════════════════════════════════════════════════
    async function loadPasswords() {
        const list = document.getElementById('passwords-list');
        list.innerHTML = '<p>Loading…</p>';
        try {
            const passwords = await apiFetch('/management/app-passwords');
            if (!passwords || passwords.length === 0) {
                list.innerHTML = '<p>No app passwords yet.</p>';
                return;
            }
            list.innerHTML = passwords.map(p => `
                <div class="card">
                    <strong>${escHtml(p.label)}</strong>
                    <div class="meta">Created: ${new Date(p.created_at).toLocaleString()} · Last used: ${p.last_used_at ? new Date(p.last_used_at).toLocaleString() : 'never'}</div>
                    <button class="btn btn-danger btn-revoke" data-id="${escHtml(p.id)}" data-label="${escHtml(p.label)}" style="margin-top:.4em">Revoke</button>
                </div>
            `).join('');

            list.querySelectorAll('.btn-revoke').forEach(btn => {
                btn.addEventListener('click', async () => {
                    if (!confirm(`Revoke "${btn.dataset.label}"?`)) return;
                    await apiFetch(`/management/app-passwords/${btn.dataset.id}`, { method: 'DELETE' });
                    loadPasswords();
                });
            });
        } catch (e) {
            list.innerHTML = `<p style="color:#f00">Error: ${escHtml(e.message)}</p>`;
        }
    }

    document.getElementById('btn-generate-password').addEventListener('click', async () => {
        const label = document.getElementById('new-password-label').value.trim();
        if (!label) { alert('Please enter a label.'); return; }
        try {
            const result = await apiFetch('/management/app-passwords', {
                method: 'POST',
                body: JSON.stringify({ label })
            });
            document.getElementById('new-password-label').value = '';

            const reveal = document.getElementById('password-reveal');
            document.getElementById('reveal-server-url').textContent = `Server: ${result.server_url}`;
            document.getElementById('reveal-username').textContent = window.ApiClient?.getCurrentUserId?.() ?? '(your username)';
            document.getElementById('reveal-password').textContent = result.password;
            document.getElementById('reveal-gpodder-url').textContent = result.gpodder_url;
            document.getElementById('reveal-opa-url').textContent = result.opa_url;
            reveal.style.display = 'block';

            loadPasswords();
        } catch (e) {
            alert(`Failed to generate password: ${e.message}`);
        }
    });

    // ══════════════════════════════════════════════════════════════════════════
    // SETTINGS TAB
    // ══════════════════════════════════════════════════════════════════════════
    async function loadSettings() {
        try {
            const cfg = await apiFetch('/management/settings');
            document.getElementById('setting-max-cache').value = cfg.max_cache_size_gb ?? 20;
            document.getElementById('setting-max-episodes').value = cfg.max_episodes_per_podcast ?? '';
            document.getElementById('setting-poll-interval').value = cfg.poll_interval_minutes ?? 60;
            document.getElementById('setting-auto-download').value = cfg.auto_download_count ?? 3;
            document.getElementById('setting-auto-download-enabled').checked = cfg.download_new_episodes ?? true;
        } catch (e) {
            console.warn('Failed to load settings', e);
        }
    }

    document.getElementById('btn-save-settings').addEventListener('click', async () => {
        const body = {
            max_cache_size_gb: parseInt(document.getElementById('setting-max-cache').value) || 20,
            max_episodes_per_podcast: document.getElementById('setting-max-episodes').value
                ? parseInt(document.getElementById('setting-max-episodes').value)
                : null,
            poll_interval_minutes: parseInt(document.getElementById('setting-poll-interval').value) || 60,
            auto_download_count: parseInt(document.getElementById('setting-auto-download').value) || 3,
            download_new_episodes: document.getElementById('setting-auto-download-enabled').checked
        };
        try {
            await apiFetch('/management/settings', { method: 'PUT', body: JSON.stringify(body) });
            const saved = document.getElementById('settings-saved');
            saved.style.display = 'inline';
            setTimeout(() => { saved.style.display = 'none'; }, 2000);
        } catch (e) {
            alert(`Failed to save: ${e.message}`);
        }
    });

    // ── Initial load ───────────────────────────────────────────────────────────
    loadSubscriptions();
    loadCache();
    loadPasswords();
    loadSettings();
})();
