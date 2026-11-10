(() => {
    'use strict';
    const app = document.querySelector('#stats-app');
    if (!app) return;
    const form = app.querySelector('#stats-filter');
    const range = form.querySelector('#stats-range');
    const custom = form.querySelector('#stats-custom');
    const start = form.querySelector('#stats-start');
    const end = form.querySelector('#stats-end');
    const dashboard = app.querySelector('#stats-dashboard');
    const status = app.querySelector('#stats-status');
    const error = app.querySelector('#stats-error');
    const apply = form.querySelector('#stats-apply');
    const integer = new Intl.NumberFormat('en-IE', { maximumFractionDigits: 0 });
    const money = new Intl.NumberFormat('en-IE', { style: 'currency', currency: 'EUR' });
    const colors = ['#047857', '#0284c7', '#7c3aed', '#d97706', '#db2777', '#475569', '#0d9488', '#4f46e5'];
    let charts = [];
    let request;
    let version = 0;

    function toggleCustom() {
        const isCustom = range.value === 'Custom';
        custom.hidden = !isCustom;
        for (const input of [start, end]) {
            input.disabled = !isCustom;
            input.required = isCustom;
        }
    }
    range.addEventListener('change', toggleCustom);
    toggleCustom();

    function destroyCharts() {
        for (const chart of charts) chart.destroy();
        charts = [];
    }

    function renderCharts() {
        const source = dashboard.querySelector('[data-stats-report]');
        if (!source) return;
        if (typeof window.Chart !== 'function') {
            dashboard.querySelector('[data-chart-error]').hidden = false;
            return;
        }
        try {
            const data = JSON.parse(source.textContent);
            const dateLabel = date => new Date(`${date}T12:00:00`).toLocaleDateString('en-GB', { day: 'numeric', month: 'short' });
            const monthLabel = date => new Date(`${date}T12:00:00`).toLocaleDateString('en-GB', { month: 'short', year: 'numeric' });
            const create = (name, type, labels, values, label, extra = {}, currency = false) => {
                const canvas = dashboard.querySelector(`[data-chart="${name}"]`);
                if (!canvas) return;
                const doughnut = type === 'doughnut';
                const horizontal = extra.indexAxis === 'y';
                const options = {
                    responsive: true,
                    maintainAspectRatio: false,
                    animation: window.matchMedia('(prefers-reduced-motion: reduce)').matches ? false : { duration: 250 },
                    plugins: {
                        legend: { display: doughnut, position: 'bottom', labels: { boxWidth: 12, padding: 14 } },
                        tooltip: { callbacks: { label: context => `${label}: ${currency ? money.format(horizontal ? context.parsed.x : context.parsed.y) : integer.format(doughnut ? context.parsed : horizontal ? context.parsed.x : context.parsed.y)}` } }
                    },
                    ...extra
                };
                if (!doughnut) {
                    options.scales = {
                        [horizontal ? 'x' : 'y']: {
                            beginAtZero: true,
                            ticks: currency ? { callback: value => money.format(value) } : { precision: 0 },
                            grid: { color: '#f1f5f9' }
                        },
                        [horizontal ? 'y' : 'x']: { grid: { display: false }, ticks: { maxRotation: 45, autoSkip: true, maxTicksLimit: horizontal ? 100 : 12 } }
                    };
                }
                charts.push(new Chart(canvas, {
                    type, data: { labels, datasets: [{
                        label, data: values,
                        backgroundColor: doughnut ? colors : type === 'line' ? 'rgba(4,120,87,0.08)' : '#047857',
                        borderColor: doughnut ? '#ffffff' : '#047857',
                        borderWidth: doughnut ? 3 : 2, borderRadius: type === 'bar' ? 5 : 0,
                        pointRadius: values.length > 60 ? 0 : 2, pointHitRadius: 10,
                        fill: type === 'line', tension: 0.2
                    }] }, options
                }));
            };
            create('revenue', 'bar', data.monthlyRevenue.map(x => monthLabel(x.month)), data.monthlyRevenue.map(x => x.revenue), 'Estimated revenue', {}, true);
            create('attendance', 'line', data.attendance.map(x => dateLabel(x.date)), data.attendance.map(x => x.checkins), 'Check-ins');
            create('memberships', 'doughnut', data.memberships.map(x => x.passName), data.memberships.map(x => x.purchases), 'Purchases', { cutout: '68%' });
            const trainerContainer = dashboard.querySelector('[data-trainer-chart-container]');
            if (trainerContainer) trainerContainer.style.height = `${Math.max(288, data.trainers.length * 34)}px`;
            create('trainers', 'bar', data.trainers.map(x => x.trainerName), data.trainers.map(x => x.checkins), 'Check-ins', { indexAxis: 'y' });
        } catch {
            destroyCharts();
            dashboard.querySelector('[data-chart-error]').hidden = false;
        }
    }

    app.addEventListener('click', event => {
        if (dashboard.getAttribute('aria-busy') === 'true' && event.target.closest('[data-stats-export]')) event.preventDefault();
    });

    form.addEventListener('submit', async event => {
        event.preventDefault();
        if (!form.reportValidity()) return;
        request?.abort();
        request = new AbortController();
        const current = ++version;
        const query = new URLSearchParams(new FormData(form));
        error.hidden = true;
        status.textContent = 'Loading statistics…';
        dashboard.setAttribute('aria-busy', 'true');
        dashboard.querySelectorAll('[data-stats-export]').forEach(link => link.setAttribute('aria-disabled', 'true'));
        apply.disabled = true;
        try {
            const response = await fetch(`${app.dataset.endpoint}?${query}`, { signal: request.signal, headers: { Accept: 'text/html' } });
            if (response.redirected || response.status === 401 || response.status === 403) throw new Error('Your session or permissions changed. Reload the page and sign in again.');
            if (!response.ok) {
                let message = 'Statistics could not be loaded. Try again.';
                if (response.status === 400 && response.headers.get('content-type')?.includes('json')) {
                    const problem = await response.json();
                    message = Object.values(problem.errors || {}).flat().join(' ') || message;
                }
                throw new Error(message);
            }
            const html = await response.text();
            if (current !== version) return;
            const template = document.createElement('template');
            template.innerHTML = html;
            if (!template.content.querySelector('[data-stats-report]')) throw new Error('The response was incomplete. Reload the page and try again.');
            destroyCharts();
            dashboard.replaceChildren(template.content);
            renderCharts();
            history.replaceState(null, '', `${form.action}?${query}`);
            form.querySelector('.validation-summary')?.remove();
            status.textContent = `Updated: ${dashboard.querySelector('[data-period-label]').textContent}.`;
        } catch (failure) {
            if (failure.name === 'AbortError' || current !== version) return;
            error.textContent = failure.message || 'Statistics could not be loaded. Try again.';
            error.hidden = false;
            status.textContent = 'The displayed statistics and export links still use the previous successful period.';
        } finally {
            if (current === version) {
                dashboard.setAttribute('aria-busy', 'false');
                dashboard.querySelectorAll('[data-stats-export]').forEach(link => link.removeAttribute('aria-disabled'));
                apply.disabled = false;
            }
        }
    });
    renderCharts();
})();
