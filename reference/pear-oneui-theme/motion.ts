/**
 * CSS 만으로는 안 되는 움직임.
 *
 * 1) ⋮ 메뉴가 닫힐 때도 애니메이션 — YT(tp-yt-iron-dropdown)는 닫을 때 바로 숨긴다(_renderClosed).
 *    그 함수를 감싸서 사라지는 애니메이션(.oneui-closing)을 먼저 보여 주고 숨긴다.
 * 2) 창 크기를 바꾸는 동안(특히 최대화) — 블러 · 전환 효과를 잠깐 꺼서 다시 그리기를 가볍게 해
 *    뚝뚝 끊기지 않게 한다. (창 자체의 크기 변화는 윈도우가 그리므로 앱이 애니메이션을 줄 수는 없다)
 */

const CLOSE_MS = 200;

type Dropdown = HTMLElement & {
  opened?: boolean;
  _renderClosed?: () => void;
  __oneuiClosing?: number;
};

export const startMotion = () => {
  // ── 1. 메뉴 닫힘 ──
  // YT(Polymer)는 메뉴 요소가 처음 만들어질 때 함수들을 다시 덮어쓴다.
  // 그래서 등록될 때 한 번, 그리고 메뉴가 열릴 때마다 감싸기가 살아 있는지 확인하고 다시 건다.
  let proto: Dropdown | undefined;
  let original: (() => void) | undefined;
  const ensurePatched = () => {
    const Ctor = customElements.get('tp-yt-iron-dropdown') as
      | (CustomElementConstructor & { prototype: Dropdown })
      | undefined;
    const p = Ctor?.prototype;
    const current = p?._renderClosed;
    if (!p || !current || (current as { __oneui?: boolean }).__oneui) return;
    proto = p;
    original = current;
    const base = current;
    const wrapped = function (this: Dropdown) {
      if (!document.documentElement.classList.contains('oneui-theme')) {
        base.call(this);
        return;
      }
      clearTimeout(this.__oneuiClosing);
      this.classList.add('oneui-closing');
      this.__oneuiClosing = window.setTimeout(() => {
        this.classList.remove('oneui-closing');
        // 그 사이 다시 열렸으면 숨기지 않는다
        if (!this.opened) base.call(this);
      }, CLOSE_MS);
    };
    (wrapped as { __oneui?: boolean }).__oneui = true;
    p._renderClosed = wrapped;
  };
  void customElements.whenDefined('tp-yt-iron-dropdown').then(ensurePatched);
  document.addEventListener('iron-overlay-opened', ensurePatched, true);

  // ── 2. 창 크기 조절 ──
  let resizeTimer = 0;
  const onResize = () => {
    document.documentElement.classList.add('oneui-resizing');
    clearTimeout(resizeTimer);
    resizeTimer = window.setTimeout(
      () => document.documentElement.classList.remove('oneui-resizing'),
      220,
    );
  };
  window.addEventListener('resize', onResize, { passive: true });

  return () => {
    document.removeEventListener('iron-overlay-opened', ensurePatched, true);
    if (proto && original) proto._renderClosed = original;
    window.removeEventListener('resize', onResize);
    clearTimeout(resizeTimer);
    document.documentElement.classList.remove('oneui-resizing');
  };
};
