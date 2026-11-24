import { bindPicturePreview } from './components/picture-preview';
import { memberships } from './components/membership';
import dashboardTemplate from './pages/dashboard.html?raw';
import privacyTemplate from './pages/privacy.html?raw';
import authTemplate from './pages/auth.html?raw';
import { statistics } from './pages/statistics';
import layout from './components/layout.html?raw';
import { request, renewCsrf, ApiError, apiBase } from './api/client';
import { escape as e, link, heading, table, field, form, pager, search } from './components/ui';
import type { Profile, Pass, Trainer, Catalog, Customer, CustomerDetails, Visit, VisitOptions, SelectOption, Dashboard, Pagination } from './types/api';

const app = document.querySelector<HTMLDivElement>('#app')!;
app.innerHTML = layout;
const main = document.querySelector<HTMLElement>('#main-content')!;
let profile: Profile | undefined;
let generation = 0;
function closeMenu(): void { document.querySelector('#navigation')!.classList.add('hidden'); document.querySelector('#menu')!.setAttribute('aria-expanded', 'false'); }
function navigate(path: string): void { closeMenu(); history.pushState(null, '', path); void render(); }
function errorText(error: unknown): string {
  return error instanceof ApiError ? [error.message, ...Object.values(error.errors).flat()].join(' ') : error instanceof Error ? error.message : 'Something went wrong.';
}
function bindForm(save: (data: FormData) => Promise<void>): void {
  document.querySelector<HTMLFormElement>('#edit-form')?.addEventListener('submit', async event => {
    event.preventDefault();
    const target = event.currentTarget as HTMLFormElement;
    const button = target.querySelector<HTMLButtonElement>('button[type=submit], button:not([type])')!;
    button.disabled = true;
    try { await save(new FormData(target)); }
    catch (error) { const box = target.querySelector<HTMLElement>('#form-error')!; box.hidden = false; box.textContent = errorText(error); }
    finally { button.disabled = false; }
  });
}
const text = (data: FormData, name: string) => String(data.get(name) ?? '');
const visitsTable = (visits: Visit[]) => table(['Customer', 'Pass', 'Check-in time', 'Trainer', ''], visits.map(v => [e(v.displayName), e(v.passName), e(v.checkedInAt.replace('T', ' ')), e(v.trainerName ?? 'No trainer'), link(`/Visits/Details/${v.id}`, 'View')]));
const select = (name: string, label: string, options: SelectOption[], value: unknown = '', optional = false) => `<div><label for="${name}" class="label">${e(label)}</label><select id="${name}" name="${name}" class="input" ${optional ? '' : 'required'}><option value="">${optional ? 'No trainer' : 'Choose an option'}</option>${options.map(o => `<option value="${e(o.value)}" ${String(value) === o.value ? 'selected' : ''}>${e(o.text)}</option>`).join('')}</select></div>`;

function navigation(): void {
  const entries = profile?.role === 'Admin' ? [['/', 'Dashboard'], ['/Customers', 'Customers'], ['/Visits', 'Check-ins'], ['/Passes', 'Gym passes'], ['/Team', 'Trainers'], ['/Stats', 'Statistics'], ['/Administration', 'Account roles'], ['/Member', 'My profile']] : [['/Member', 'My profile'], ['/Visits', 'My check-ins']];
  document.querySelector('#links')!.innerHTML = entries.map(([url, title]) => link(url, title, 'nav-link')).join('');
  document.querySelector('#identity')!.textContent = profile ? `${profile.fullName} · ${profile.role}` : '';
  document.querySelector<HTMLButtonElement>('#logout')!.hidden = !profile;
}

async function authPage(register: boolean): Promise<void> {
  main.innerHTML = authTemplate;
  document.querySelector('#auth-heading')!.innerHTML = heading(register ? 'Create your account' : 'Sign in', 'Access your fitness account.');
  document.querySelector('#auth-fields')!.innerHTML = form((register ? field('fullName', 'Full name') + field('dateofbirth', 'Date of birth', '', 'date') : '') + field('email', 'Email', '', 'email') + field('password', 'Password', '', 'password') + (register ? field('confirmPassword', 'Confirm password', '', 'password') : ''), register ? 'Create account' : 'Sign in') + link(register ? '/Account/Login' : '/Account/Register', register ? 'Sign in' : 'Create an account');
  bindForm(async data => {
    if (register && text(data, 'password') !== text(data, 'confirmPassword')) throw new Error('Passwords must match.');
    await renewCsrf();
    await request(register ? '/api/session/registration' : '/api/session', 'POST', Object.fromEntries(data));
    await renewCsrf();
    profile = await request<Profile>('/api/users/me');
    const returnUrl = new URLSearchParams(location.search).get('returnUrl');
    navigate(returnUrl?.startsWith('/') && !returnUrl.startsWith('//') ? returnUrl : '/');
  });
}

async function catalog(section: string, action: string, id: number): Promise<void> {
  const passes = section === 'passes';
  const endpoint = passes ? '/api/passes' : '/api/trainers';
  const route = passes ? '/Passes' : '/Team';
  if (action === 'index') {
    const data = await request<Catalog<Pass | Trainer>>(endpoint + location.search);
    main.innerHTML = heading(passes ? 'Gym passes' : 'Trainers', passes ? "Manage your club's subscription types, prices, and visit limits." : 'Manage your training team.') + link(route + '/Create', passes ? 'Add pass type' : 'Add trainer', 'btn btn-primary mb-5') + search() + table(passes ? ['Pass type', 'Price', 'Visits', 'Validity', 'Actions'] : ['Full name', 'Specialty', 'Phone', 'Actions'], data.items.map(item => {
      const cells = 'name' in item ? [link(`${route}/Details/${item.id}`, item.name), e(item.price.toFixed(2)), e(item.maxVisits), `${item.validityDays} days`] : [link(`${route}/Details/${item.id}`, item.fullName), e(item.specialty), e(item.phone)];
      return [...cells, link(`${route}/Edit/${item.id}`, 'Edit') + ' · ' + link(`${route}/Delete/${item.id}`, 'Delete')];
    })) + pager(data.page, data.totalPages);
    return;
  }
  const item = action === 'create' ? undefined : await request<Pass | Trainer>(`${endpoint}/${id}`);
  const title = item ? ('name' in item ? item.name : item.fullName) : passes ? 'Add pass type' : 'Add trainer';
  if (action === 'details' || action === 'delete') {
    main.innerHTML = heading(action === 'delete' ? `Delete ${title}?` : title) + table(['Field', 'Value'], Object.entries(item!).map(([key, value]) => [e(key), e(value)])) + (action === 'delete' ? form('<p>Deletion is unavailable for records used by memberships or visits.</p>', 'Delete') : link(`${route}/Edit/${id}`, 'Edit', 'btn btn-secondary')) + link(route, 'Back', 'btn btn-secondary');
    if (action === 'delete') bindForm(async () => { await request(`${endpoint}/${id}`, 'DELETE'); navigate(route); });
    return;
  }
  const pass = item as Pass | undefined;
  const trainer = item as Trainer | undefined;
  main.innerHTML = heading(title) + form(passes ? field('name', 'Pass name', pass?.name) + field('price', 'Price', pass?.price ?? 0, 'number') + field('maxVisits', 'Maximum visits', pass?.maxVisits ?? 1, 'number') + field('validityDays', 'Validity in days', pass?.validityDays ?? 1, 'number') : field('fullName', 'Full name', trainer?.fullName) + field('specialty', 'Specialty', trainer?.specialty) + field('phone', 'Phone number', trainer?.phone, 'tel')) + link(route, 'Cancel');
  bindForm(async data => {
    const body = passes ? { name: text(data, 'name'), price: Number(data.get('price')), maxVisits: Number(data.get('maxVisits')), validityDays: Number(data.get('validityDays')) } : Object.fromEntries(data);
    const created = await request<{ id: number } | undefined>(endpoint + (item ? `/${id}` : ''), item ? 'PUT' : 'POST', body);
    navigate(`${route}/Details/${created?.id ?? id}`);
  });
}

async function customerPage(section: string, action: string, id: number): Promise<void> {
  if (section === 'customers' && action === 'index') {
    const result = await request<{ customers: Customer[]; pagination: Pagination }>('/api/customers' + location.search);
    main.innerHTML = heading('Customers', 'Find a customer, check their pass, and confirm their entry.') + link('/Customers/Create', 'New customer', 'btn btn-primary mb-5') + search('query') + table(['Customer', 'Email', 'Active passes', ''], result.customers.map(c => [e(c.displayName), e(c.email), e(c.activePasses), link(`/Customers/Details/${c.id}`, 'View profile')])) + pager(result.pagination.page, result.pagination.totalPages);
    return;
  }
  if (action === 'create') {
    main.innerHTML = heading('New customer') + '<div class="panel panel-body"><p>Receptionist customer creation is not available. A customer can create their own account. Registration signs in to the newly created account.</p>' + link('/Account/Register', 'Open account registration') + '</div>';
    return;
  }
  if (action === 'purchase') {
    const data = await request<{ customerName: string; passTypes: { id: number; name: string; price: number; visits: number; validityDays: number }[] }>(`/api/customers/${id}/pass-options`);
    main.innerHTML = heading('Sell a pass', data.customerName) + form(select('passTypeId', 'Pass type', data.passTypes.map(p => ({ value: String(p.id), text: `${p.name} · ${p.price.toFixed(2)} · ${p.visits} visits · ${p.validityDays} days` }))) + '<p>Payment is collected separately.</p>', 'Confirm and assign pass');
    bindForm(async data => { await request(`/api/customers/${id}/passes`, 'POST', { passTypeId: Number(data.get('passTypeId')) }); navigate(`/Customers/Details/${id}`); });
    return;
  }
  const own = section === 'member';
  const data = await request<CustomerDetails>(own ? '/api/members/me' : `/api/customers/${id}`);
  main.innerHTML = heading(data.displayName, 'Customer profile and membership passes.') + (!own ? link(`/Customers/Purchase/${id}`, 'Sell a pass', 'btn btn-secondary mb-5') : '') + `<section class="panel panel-body"><h2 class="text-lg font-semibold">Personal information</h2><p>${e(data.email)}</p><p>${e(data.dateOfBirth)}</p>${data.profilePictureUrl ? `<img width="96" height="96" alt="Profile picture" src="${e(apiBase + data.profilePictureUrl.replace(/^~/, ''))}">` : ''}</section>` + (own ? form(field('file', 'Profile picture', '', 'file'), 'Upload picture') + '<button id="remove-picture" class="btn btn-secondary">Remove picture</button>' : '') + '<h2 class="mt-7 text-lg font-semibold">Membership passes</h2>' + memberships(data.passes) + '<h2 class="mt-7 text-lg font-semibold">Recent visits</h2>' + visitsTable(data.recentVisits);
  if (own) { bindPicturePreview();
    bindForm(async data => { await request('/api/users/me/profile-picture', 'POST', data); await render(); });
    document.querySelector('#remove-picture')?.addEventListener('click', async () => { try { await request('/api/users/me/profile-picture', 'DELETE'); await render(); } catch (error) { alert(errorText(error)); } });
  }
}

async function visitPage(action: string, id: number): Promise<void> {
  if (action === 'index') {
    const data = await request<{ visits: Visit[]; pagination: Pagination }>('/api/visits' + location.search);
    main.innerHTML = heading('Check-ins', 'Entry history and trainer-assisted visits.') + link('/Visits/Create', 'New check-in', 'btn btn-primary mb-5') + search('query') + visitsTable(data.visits) + pager(data.pagination.page, data.pagination.totalPages);
    return;
  }
  if (action === 'create') {
    const options = await request<VisitOptions>('/api/visits/options' + location.search);
    main.innerHTML = heading('Confirm entry', 'Each pass can be checked in once per day.') + (profile?.role === 'Admin' ? search('query') : '') + form(select('customerPassId', 'Customer pass', options.passes.filter(p => !p.checkedInToday).map(p => ({ value: String(p.id), text: `${p.customerName} · ${p.name} · ${p.remainingVisits} visits · ${p.expires}` })), new URLSearchParams(location.search).get('passId') ?? '') + select('trainerId', 'Trainer (optional)', options.trainers, '', true) + '<p>Confirmation deducts one visit from the selected pass.</p>', 'Confirm entry');
    bindForm(async data => { const created = await request<{ id: number }>('/api/visits', 'POST', { customerPassId: Number(data.get('customerPassId')), trainerId: data.get('trainerId') ? Number(data.get('trainerId')) : null }); navigate(`/Visits/Details/${created.id}`); });
    return;
  }
  const visit = await request<Visit>(`/api/visits/${id}`);
  main.innerHTML = heading(action === 'delete' ? 'Remove this check-in?' : action === 'edit' ? 'Edit trainer' : `Check-in #${id}`) + visitsTable([visit]);
  if (action === 'edit') {
    const trainers = await request<SelectOption[]>('/api/visits/trainers');
    main.innerHTML += form(select('trainerId', 'Trainer (optional)', trainers, visit.trainerId, true), 'Save trainer');
    bindForm(async data => { await request(`/api/visits/${id}`, 'PUT', { id, trainerId: data.get('trainerId') ? Number(data.get('trainerId')) : null }); navigate(`/Visits/Details/${id}`); });
  } else if (action === 'delete') {
    main.innerHTML += form('<p>One visit will be restored to the pass.</p>', 'Remove and restore visit');
    bindForm(async () => { await request(`/api/visits/${id}`, 'DELETE'); navigate('/Visits'); });
  } else if (profile?.role === 'Admin') main.innerHTML += link(`/Visits/Edit/${id}`, 'Edit trainer', 'btn btn-secondary') + link(`/Visits/Delete/${id}`, 'Remove check-in', 'btn btn-danger') + link(`/Customers/Details/${visit.customerId}`, 'Customer profile', 'btn btn-secondary');
}

async function render(): Promise<void> {
  const sequence = ++generation;
  main.innerHTML = '<p role="status">Loading…</p>';
  const parts = location.pathname.split('/').filter(Boolean);
  let section = (parts[0] ?? 'home').toLowerCase();
  const action = (parts[1] ?? 'index').toLowerCase();
  const id = Number(parts[2] ?? new URLSearchParams(location.search).get('id'));
  try {
    if (section === 'account' && action === 'accessdenied') throw new ApiError(403, 'You do not have permission to view this page.');
    if (section === 'account') { navigation(); await authPage(action === 'register'); return; }
    if (section === 'home' && action === 'privacy') { main.innerHTML = privacyTemplate; return; }
    profile ??= await request<Profile>('/api/users/me');
    if (sequence !== generation) return;
    navigation();
    if (section === 'home') section = profile.role === 'Admin' ? 'dashboard' : 'member';
    if (profile.role !== 'Admin' && ['dashboard', 'customers', 'passes', 'team', 'stats', 'administration'].includes(section)) throw new ApiError(403, 'You do not have permission to view this page.');
    if (section === 'passes' || section === 'team') await catalog(section, action, id);
    else if (section === 'member' || section === 'customers') await customerPage(section, action, id);
    else if (section === 'visits' || section === 'checkin') await visitPage(action, id);
    else if (section === 'dashboard') {
      const data = await request<Dashboard>('/api/dashboard');
      main.innerHTML = dashboardTemplate;
      document.querySelector('#statistics-cards')!.innerHTML = [['Check-ins today', data.todayVisits], ['With a trainer today', data.trainerVisits], ['Active passes', data.activePasses], ['Registered customers', data.customers]].map(([name, count]) => `<div class="panel"><div class="panel-body"><p class="text-sm font-medium text-slate-500">${name}</p><p class="mt-3 text-3xl font-bold">${count}</p></div></div>`).join('');
      document.querySelector('#recent-visits')!.innerHTML = visitsTable(data.recentVisits);
    } else if (section === 'administration') {
      const data = await request<{ accounts: { id: number; name: string; email: string; role: string }[]; pagination: Pagination }>('/api/accounts' + location.search);
      main.innerHTML = heading('Account roles') + search() + table(['Name', 'Email', 'Role', ''], data.accounts.map(a => [e(a.name), e(a.email), e(a.role), `<button class="btn btn-secondary" data-role-id="${a.id}" data-role="${a.role === 'Admin' ? 'User' : 'Admin'}">Make ${a.role === 'Admin' ? 'User' : 'Admin'}</button>`])) + pager(data.pagination.page, data.pagination.totalPages);
      main.querySelectorAll<HTMLButtonElement>('[data-role-id]').forEach(button => button.addEventListener('click', async () => { try { await request(`/api/accounts/${button.dataset.roleId}/role`, 'PUT', { id: Number(button.dataset.roleId), role: button.dataset.role }); profile = undefined; await render(); } catch (error) { alert(errorText(error)); } }));
    } else if (section === 'stats') {
      await statistics(main);
    } else throw new ApiError(404, 'Page not found.');
    document.title = `${main.querySelector('h1')?.textContent ?? 'FitnessApp'} · FitnessApp`;
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) { profile = undefined; navigate('/Account/Login?returnUrl=' + encodeURIComponent(location.pathname + location.search)); return; }
    main.innerHTML = heading(error instanceof ApiError && error.status === 403 ? 'Access denied' : 'Unable to load this page') + `<div class="notice notice-error" role="alert">${e(errorText(error))}</div>` + link('/', 'Return home');
  }
}

document.addEventListener('click', event => {
  if (!(event.target instanceof Element)) return;
  const anchor = event.target.closest<HTMLAnchorElement>('a[href]');
  if (!anchor || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button !== 0 || anchor.target || anchor.download) return;
  const url = new URL(anchor.href);
  if (url.origin !== location.origin || url.hash) return;
  event.preventDefault(); navigate(url.pathname + url.search);
});
window.addEventListener('popstate', () => void render());
document.querySelector('#close-menu')!.addEventListener('click', closeMenu);
document.addEventListener('keydown', event => { if (event.key === 'Escape') closeMenu(); });
document.querySelector('#menu')!.addEventListener('click', () => { const panel = document.querySelector('#navigation')!; panel.classList.toggle('hidden'); document.querySelector('#menu')!.setAttribute('aria-expanded', String(!panel.classList.contains('hidden'))); });
document.querySelector('#logout')!.addEventListener('click', async () => { try { await renewCsrf(); await request('/api/session', 'DELETE'); profile = undefined; await renewCsrf(); navigate('/Account/Login'); } catch (error) { alert(errorText(error)); } });
void render();






