export function bindPicturePreview(): void {
  const input = document.querySelector<HTMLInputElement>('#file');
  if (!input) return;
  input.accept = '.jpg,.jpeg,.png,.webp,image/jpeg,image/png,image/webp';
  const preview = document.createElement('img');
  preview.alt = 'Selected profile picture preview';
  preview.width = 112; preview.height = 112;
  preview.className = 'size-28 rounded-2xl border border-slate-200 object-contain';
  preview.hidden = true;
  const cancel = document.createElement('button');
  cancel.type = 'button'; cancel.className = 'btn btn-secondary'; cancel.textContent = 'Cancel preview'; cancel.hidden = true;
  input.after(preview, cancel);
  cancel.addEventListener('click', () => { input.value = ''; preview.hidden = true; cancel.hidden = true; });
  input.addEventListener('change', () => {
    const file = input.files?.[0];
    if (!file) return;
    const url = URL.createObjectURL(file);
    preview.onload = () => URL.revokeObjectURL(url);
    preview.onerror = () => { URL.revokeObjectURL(url); preview.hidden = true; };
    preview.src = url; preview.hidden = false; cancel.hidden = false;
  });
}
