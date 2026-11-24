import { request, download } from '../api/client';
import { chart } from '../components/charts';
import { heading, table, escape as e } from '../components/ui';

interface Report {
  period: { label: string }; pdfAvailable: boolean;
  totalCustomers: number; activeMemberships: number; allTimeEstimatedRevenue: number;
  totalCheckins: number; totalTrainers: number; periodCheckins: number;
  periodPurchases: number; periodPurchasingCustomers: number; periodEstimatedRevenue: number;
  monthlyRevenue: { month: string; purchases: number; revenue: number }[];
  attendance: { date: string; checkins: number }[];
  memberships: { passName: string; purchases: number; revenue: number }[];
  trainers: { trainerName: string; specialty: string; checkins: number }[];
}
export async function statistics(main: HTMLElement): Promise<void> {
  const query = location.search;
  const report = await request<Report>('/api/statistics/Data' + query);
  const params = new URLSearchParams(query);
  const ranges = ['Today', 'Last7Days', 'Last30Days', 'ThisMonth', 'ThisYear', 'Custom'];
  main.innerHTML = heading('Statistics', 'Club performance and membership activity.') + `<form method="get" class="panel panel-body flex flex-wrap gap-3"><label>Period<select name="range" class="input">${ranges.map(r => `<option ${r === (params.get('range') ?? 'Last30Days') ? 'selected' : ''}>${r}</option>`).join('')}</select></label><label>Start<input type="date" class="input" name="start" value="${e(params.get('start'))}"></label><label>End<input type="date" class="input" name="end" value="${e(params.get('end'))}"></label><button class="btn btn-primary">Apply</button></form><div class="my-5 flex gap-3"><h2 class="text-lg font-bold">Club overview · ${e(report.period.label)}</h2>${report.pdfAvailable ? '<button class="btn btn-secondary" data-export="Pdf">PDF report</button>' : '<span>PDF unavailable</span>'}<button class="btn btn-secondary" data-export="Excel">Excel report</button></div>`;
  main.innerHTML += '<div class="mb-6 grid gap-4 sm:grid-cols-2 2xl:grid-cols-3">' + [['Total customers', report.totalCustomers], ['Active memberships', report.activeMemberships], ['Estimated revenue', report.allTimeEstimatedRevenue.toFixed(2) + ' EUR'], ['Total check-ins', report.totalCheckins], ['Total trainers', report.totalTrainers], ['Period check-ins', report.periodCheckins]].map(([title, value]) => `<section class="panel panel-body"><h3>${title}</h3><p class="text-3xl font-bold">${value}</p></section>`).join('') + '</div>';
  main.innerHTML += `<section class="mb-6 rounded-2xl bg-emerald-900 p-5 text-white"><h2>Selected period</h2><div class="grid gap-6 sm:grid-cols-3"><p>Estimated revenue<br>${report.periodEstimatedRevenue.toFixed(2)} EUR</p><p>Membership purchases<br>${report.periodPurchases}</p><p>Purchasing customers<br>${report.periodPurchasingCustomers}</p></div></section><p class="notice notice-info mb-6">Estimated revenue uses current gym pass prices. Historical prices paid and payment transactions are not recorded; this is not an accounting revenue total.</p>`;
  const sections: [string, string[], string[][]][] = [
    ['Monthly estimated revenue', ['Month', 'Purchases', 'Estimated EUR'], report.monthlyRevenue.map(r => [e(r.month), e(r.purchases), r.revenue.toFixed(2)])],
    ['Attendance', ['Date', 'Check-ins'], report.attendance.map(r => [e(r.date), e(r.checkins)])],
    ['Membership purchases', ['Pass', 'Purchases', 'Estimated EUR'], report.memberships.map(r => [e(r.passName), e(r.purchases), r.revenue.toFixed(2)])],
    ['Trainer activity', ['Trainer', 'Specialty', 'Check-ins'], report.trainers.map(r => [e(r.trainerName), e(r.specialty), e(r.checkins)])]
  ];
  const charts = [
    chart('Monthly estimated revenue', report.monthlyRevenue.map(r => ({ label: r.month, value: r.revenue })), 'bar'),
    chart('Daily attendance', report.attendance.map(r => ({ label: r.date, value: r.checkins })), 'line'),
    chart('Most popular memberships', report.memberships.map(r => ({ label: r.passName, value: r.purchases })), 'doughnut'),
    chart('Trainer activity', report.trainers.map(r => ({ label: r.trainerName, value: r.checkins })), 'horizontal')
  ];
  main.innerHTML += '<div class="grid gap-6 xl:grid-cols-2">' + sections.map(([title, headers, rows], index) => `<section class="panel panel-body"><h2 class="text-lg font-bold">${title}</h2>${charts[index]}<details><summary class="text-link">View data</summary>${table(headers, rows)}</details></section>`).join('') + '</div>';
  main.querySelectorAll<HTMLButtonElement>('[data-export]').forEach(button => button.addEventListener('click', async () => {
    button.disabled = true;
    try {
      const url = URL.createObjectURL(await download(`/api/statistics/Export${button.dataset.export}${query}`));
      const anchor = document.createElement('a'); anchor.href = url; anchor.download = `FitnessApp-statistics.${button.dataset.export === 'Pdf' ? 'pdf' : 'xlsx'}`; anchor.click(); URL.revokeObjectURL(url);
    } catch (error) { alert(error instanceof Error ? error.message : 'Report download failed.'); }
    finally { button.disabled = false; }
  }));
}


