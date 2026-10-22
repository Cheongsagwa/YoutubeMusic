/**
 * 상단 바(ytmusic-nav-bar) 안쪽 요소는 backdrop-filter 가 페이지 내용을 보지 못해 블러가 먹지 않는다.
 * (검색창 · 검색 추천 목록이 여기 있다)
 *
 * 그래서 본문 쪽(body 바로 아래)에 블러만 하는 '대역' 상자를 두고,
 * 대상 요소 바로 밑에 같은 위치 · 크기 · 둥근 모서리로 겹쳐 따라다니게 한다.
 * 대상은 그 위에 반투명하게 그려지므로 결과적으로 유리(블러)처럼 보인다.
 */

type Target = {
  selector: string;
  /** 보여야 할 때만 true */
  active: (el: HTMLElement) => boolean;
};

const TARGETS: Target[] = [
  {
    selector: 'ytmusic-nav-bar ytmusic-search-box .search-box',
    active: () => true,
  },
  {
    selector: 'ytmusic-nav-bar ytmusic-search-box #suggestion-list',
    active: (el) =>
      !!el.closest('ytmusic-search-box')?.hasAttribute('opened') && el.offsetHeight > 20,
  },
];

export const startBackdropProxy = () => {
  const proxies = TARGETS.map(() => {
    const p = document.createElement('div');
    p.className = 'oneui-bf-proxy';
    document.body.appendChild(p);
    return p;
  });
  const lastKey: string[] = TARGETS.map(() => '');

  let frame = 0;
  let last = 0;
  const tick = (now: number) => {
    frame = requestAnimationFrame(tick);
    if (now - last < 1000 / 60 - 1) return;
    last = now;
    // 재생 화면이 열려 상단 바가 숨겨졌으면 대역도 숨긴다
    const navHidden = !!document
      .querySelector('ytmusic-app-layout')
      ?.hasAttribute('player-page-open');
    TARGETS.forEach((t, i) => {
      const p = proxies[i];
      const el = document.querySelector<HTMLElement>(t.selector);
      if (!el || navHidden || !t.active(el)) {
        if (lastKey[i] !== 'off') {
          p.style.opacity = '0';
          lastKey[i] = 'off';
        }
        return;
      }
      const r = el.getBoundingClientRect();
      const radius = getComputedStyle(el).borderRadius;
      const key = `${r.left}|${r.top}|${r.width}|${r.height}|${radius}`;
      if (key === lastKey[i]) return;
      lastKey[i] = key;
      p.style.left = `${r.left}px`;
      p.style.top = `${r.top}px`;
      p.style.width = `${r.width}px`;
      p.style.height = `${r.height}px`;
      p.style.borderRadius = radius;
      p.style.opacity = '1';
    });
  };
  frame = requestAnimationFrame(tick);

  return () => {
    cancelAnimationFrame(frame);
    proxies.forEach((p) => p.remove());
  };
};
