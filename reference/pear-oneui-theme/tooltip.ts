/**
 * 마우스를 올렸을 때 뜨는 설명(title 속성)은 윈도우 기본 툴팁으로 떠서 웹 느낌이 난다.
 * title 을 잠시 빼 두고, 그 자리에 One UI 유리 말풍선(둥근 모서리 · 블러 · 테두리 없음)을 띄운다.
 * (스크린 리더용으로 aria-label 이 없으면 채워 둔다)
 */

const DELAY = 450;

export const startTooltips = () => {
  const tip = document.createElement('div');
  tip.className = 'oneui-tooltip';
  tip.setAttribute('role', 'tooltip');
  document.body.appendChild(tip);

  let timer = 0;
  let current: HTMLElement | null = null;

  const hide = () => {
    clearTimeout(timer);
    tip.classList.remove('show');
    current = null;
  };

  const show = (el: HTMLElement, text: string) => {
    tip.textContent = text;
    tip.classList.add('measure');
    const r = el.getBoundingClientRect();
    const t = tip.getBoundingClientRect();
    tip.classList.remove('measure');
    const gap = 8;
    let top = r.bottom + gap;
    // 아래 공간이 모자라면 위로 (재생 바 버튼 등)
    if (top + t.height > window.innerHeight - 8) top = r.top - t.height - gap;
    let left = r.left + r.width / 2 - t.width / 2;
    left = Math.max(8, Math.min(left, window.innerWidth - t.width - 8));
    tip.style.left = `${Math.round(left)}px`;
    tip.style.top = `${Math.round(top)}px`;
    tip.classList.toggle('above', top < r.top);
    tip.classList.add('show');
  };

  const onOver = (e: MouseEvent) => {
    const target = e.target instanceof Element ? e.target : null;
    const el = target?.closest<HTMLElement>('[title], [data-oneui-title]');
    if (!el || el === current) return;
    if (el.matches('input, textarea')) return;
    // YT 가 title 을 다시 써 넣었을 수 있으니 매번 옮겨 둔다
    const raw = el.getAttribute('title');
    if (raw !== null) {
      if (raw.trim()) el.dataset.oneuiTitle = raw;
      el.removeAttribute('title');
      if (raw.trim() && !el.hasAttribute('aria-label')) el.setAttribute('aria-label', raw);
    }
    const text = el.dataset.oneuiTitle;
    hide();
    if (!text) return;
    current = el;
    timer = window.setTimeout(() => {
      if (current === el && el.isConnected) show(el, text);
    }, DELAY);
  };

  const onOut = (e: MouseEvent) => {
    if (!current) return;
    const to = e.relatedTarget instanceof Node ? e.relatedTarget : null;
    if (to && current.contains(to)) return;
    hide();
  };

  document.addEventListener('mouseover', onOver, true);
  document.addEventListener('mouseout', onOut, true);
  document.addEventListener('mousedown', hide, true);
  document.addEventListener('wheel', hide, { capture: true, passive: true });

  return () => {
    hide();
    document.removeEventListener('mouseover', onOver, true);
    document.removeEventListener('mouseout', onOut, true);
    document.removeEventListener('mousedown', hide, true);
    document.removeEventListener('wheel', hide, { capture: true });
    tip.remove();
    // 빼 둔 title 을 돌려놓는다
    document.querySelectorAll<HTMLElement>('[data-oneui-title]').forEach((el) => {
      el.setAttribute('title', el.dataset.oneuiTitle ?? '');
      delete el.dataset.oneuiTitle;
    });
  };
};
