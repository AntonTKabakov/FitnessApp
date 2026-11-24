import formTemplate from './form.html?raw';
export const escape = (value: unknown): string => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]!);
export const link = (url: string, text: string, style = 'text-link') => `<a class="${style}" href="${escape(url)}">${escape(text)}</a>`;
export const heading = (title: string, description = '') => `<div class="mb-7"><h1 class="page-heading">${escape(title)}</h1><p class="page-description">${escape(description)}</p></div>`;
export function table(headers: string[], rows: string[][]): string {
  return rows.length ? `<div class="panel table-wrap"><table class="data-table"><thead><tr>${headers.map(h => `<th>${escape(h)}</th>`).join('')}</tr></thead><tbody>${rows.map(row => `<tr>${row.map(cell => `<td>${cell}</td>`).join('')}</tr>`).join('')}</tbody></table></div>` : '<div class="panel empty-state">No records found.</div>';
}
export function field(name: string, label: string, value: unknown = '', type = 'text', required = true): string {
  return `<div><label class="label" for="${name}">${escape(label)}</label><input class="input" id="${name}" name="${name}" type="${type}" value="${escape(value)}" ${required ? 'required' : ''} ${type === 'number' ? 'min="0" step="any"' : ''}></div>`;
}
export const form = (fields: string, label = 'Save changes') => formTemplate.replace('<div data-fields class="space-y-6"></div>', `<div data-fields class="space-y-6">${fields}</div>`).replace('type="submit"></button>', `type="submit">${escape(label)}</button>`);
export function pager(page: number, totalPages: number): string {
  const url = new URL(location.href);
  const item = (n: number, label: string) => { url.searchParams.set('page', String(n)); return link(url.pathname + url.search, label, 'btn btn-secondary'); };
  return `<div class="mt-5 flex gap-3">${page > 1 ? item(page - 1, 'Previous') : ''}<span>Page ${page} of ${totalPages}</span>${page < totalPages ? item(page + 1, 'Next') : ''}</div>`;
}
export const search = (name = 'search') => `<form method="get" class="mb-5 flex flex-wrap gap-3"><input class="input" aria-label="Search" name="${name}" value="${escape(new URLSearchParams(location.search).get(name) ?? '')}" placeholder="Search">${/^\/(Visits|Checkin)\/?$/i.test(location.pathname) ? `<label>Date<input class="input" type="date" name="date" value="${escape(new URLSearchParams(location.search).get('date'))}"></label>` : ''}<button class="btn btn-secondary">Search</button><a class="btn btn-secondary" href="${escape(location.pathname)}">Clear</a></form>`;

