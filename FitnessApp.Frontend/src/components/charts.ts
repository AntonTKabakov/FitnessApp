import { escape as e } from './ui';
export function chart(label: string, entries: { label: string; value: number }[], kind: 'bar' | 'line' | 'doughnut' | 'horizontal'): string {
  if (!entries.length || entries.every(item => item.value === 0)) return '<p class="empty-state">No activity in this period.</p>';
  const colors = ['#047857', '#0d9488', '#059669', '#34d399', '#6ee7b7', '#064e3b'];
  const max = Math.max(1, ...entries.map(item => item.value));
  let shapes = '';
  if (kind === 'doughnut') {
    const total = entries.reduce((sum, item) => sum + item.value, 0);
    let offset = 0;
    shapes = entries.map((item, index) => {
      const portion = item.value / total * 100;
      const circle = `<circle cx="240" cy="125" r="80" fill="none" stroke="${colors[index % colors.length]}" stroke-width="45" pathLength="100" stroke-dasharray="${portion} ${100 - portion}" stroke-dashoffset="${-offset}" transform="rotate(-90 240 125)"><title>${e(item.label)}: ${item.value}</title></circle>`;
      offset += portion;
      return circle;
    }).join('');
  } else if (kind === 'horizontal') {
    const height = 200 / entries.length;
    shapes = entries.map((item, index) => `<text x="0" y="${index * height + height / 2 + 4}" font-size="11" fill="#475569">${e(item.label.slice(0, 20))}</text><rect x="150" y="${index * height + 3}" width="${item.value / max * 290}" height="${Math.max(1, height - 6)}" rx="3" fill="#047857"><title>${e(item.label)}: ${item.value}</title></rect>`).join('');
  } else {
    const width = 420 / entries.length;
    if (kind === 'line') shapes = `<polyline fill="none" stroke="#047857" stroke-width="2" points="${entries.map((item, index) => `${30 + index * width + width / 2},${215 - item.value / max * 190}`).join(' ')}"/>`;
    shapes += entries.map((item, index) => {
      const x = 30 + index * width;
      const y = 215 - item.value / max * 190;
      return (kind === 'line' ? `<circle cx="${x + width / 2}" cy="${y}" r="3" fill="#047857"><title>${e(item.label)}: ${item.value}</title></circle>` : `<rect x="${x + 2}" y="${y}" width="${Math.max(1, width - 4)}" height="${215 - y}" rx="3" fill="#047857"><title>${e(item.label)}: ${item.value}</title></rect>`) + (index % Math.ceil(entries.length / 6) === 0 ? `<text x="${x}" y="240" font-size="10" fill="#475569">${e(item.label.slice(0, 10))}</text>` : '');
    }).join('');
  }
  return `<svg viewBox="0 0 480 260" role="img" aria-label="${e(label)}" class="mt-6 w-full"><title>${e(label)}. Exact values are provided in the table below.</title>${shapes}</svg>`;
}
