/**
 * One UI 아이콘으로 바꿔 끼우기.
 *
 * YouTube Music 아이콘(svg)은 숨기고, 그 자리(아이콘 칸)에 One UI 아이콘을 CSS 마스크로 그린다.
 * 그래서 색은 원래 글자색(currentColor)을 그대로 따라가고, 버튼 크기 · 눌림 효과도 그대로다.
 *
 * - search · more · back · close · add 는 삼성 sesl-androidx(One UI 공식 라이브러리)의 경로를 그대로 썼다
 * - 미디어 아이콘(재생 · 이전 · 다음 · 반복 · 셔플 · 볼륨 · 좋아요)은 sesl 에 없어서
 *   같은 규칙(24 격자, 둥근 끝 · 둥근 모서리, 1.7 선 굵기)으로 맞춰 그렸다
 */

const STROKE =
  'fill="none" stroke="#000" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"';

const svg = (body: string, viewBox = '0 0 24 24') =>
  `<svg xmlns="http://www.w3.org/2000/svg" viewBox="${viewBox}">${body}</svg>`;

const THUMB =
  'M7.6 10.6v8.9M7.6 10.6l3.5-6.1c.3-.6 1-.9 1.6-.7 1 .3 1.6 1.3 1.4 2.3l-.6 3.2h4.3c1.3 0 2.3 1.2 2 2.5l-1.4 5.8c-.2 1-1.1 1.7-2.1 1.7H7.6M7.6 10.6H5.2c-.6 0-1.1.5-1.1 1.1v6.7c0 .6.5 1.1 1.1 1.1h2.4';

export const ICONS = {
  // ── 재생 바 ──
  play: svg(
    '<path d="M8 5.7v12.6c0 1 1.1 1.6 1.9 1.1l9.8-6.3c.8-.5.8-1.7 0-2.2L9.9 4.6C9.1 4.1 8 4.7 8 5.7z"/>',
  ),
  pause: svg(
    '<rect x="6.2" y="4.8" width="4" height="14.4" rx="1.6"/><rect x="13.8" y="4.8" width="4" height="14.4" rx="1.6"/>',
  ),
  previous: svg(
    '<rect x="4.8" y="5.2" width="2.2" height="13.6" rx="1.1"/><path d="M19.2 6.7v10.6c0 .9-1 1.4-1.7.9l-7.6-5.3c-.6-.4-.6-1.4 0-1.8l7.6-5.3c.7-.5 1.7 0 1.7.9z"/>',
  ),
  next: svg(
    '<rect x="17" y="5.2" width="2.2" height="13.6" rx="1.1"/><path d="M4.8 6.7v10.6c0 .9 1 1.4 1.7.9l7.6-5.3c.6-.4.6-1.4 0-1.8L6.5 5.8c-.7-.5-1.7 0-1.7.9z"/>',
  ),
  repeat: svg(
    `<path ${STROKE} d="M16.8 3.6 19.4 6.2 16.8 8.8M4.6 11.2V9.4c0-1.8 1.4-3.2 3.2-3.2h11.6M7.2 20.4 4.6 17.8 7.2 15.2M19.4 12.8v1.8c0 1.8-1.4 3.2-3.2 3.2H4.6"/>`,
  ),
  repeatOne: svg(
    `<path ${STROKE} d="M16.8 3.6 19.4 6.2 16.8 8.8M4.6 11.2V9.4c0-1.8 1.4-3.2 3.2-3.2h11.6M7.2 20.4 4.6 17.8 7.2 15.2M19.4 12.8v1.8c0 1.8-1.4 3.2-3.2 3.2H4.6"/><path ${STROKE} d="M11 10.8l1.3-.9v4.4"/>`,
  ),
  shuffle: svg(
    `<path ${STROKE} d="M16.6 3.8 19.4 6.6 16.6 9.4M4.6 17.4h2.6c1.2 0 2.3-.6 3-1.6l3.8-5.6c.7-1 1.8-1.6 3-1.6h2.4M16.6 14.6l2.8 2.8-2.8 2.8M4.6 6.6h2.6c1.2 0 2.3.6 3 1.6l.6.9M13.6 15l.4.6c.7 1 1.8 1.6 3 1.6h2.4"/>`,
  ),
  volume: svg(
    `<path d="M4 9.6v4.8c0 .7.5 1.2 1.2 1.2h2.5l4.1 3.5c.7.6 1.7.1 1.7-.8V5.7c0-.9-1-1.4-1.7-.8L7.7 8.4H5.2C4.5 8.4 4 8.9 4 9.6z"/><path ${STROKE} d="M16.4 8.9a4.4 4.4 0 0 1 0 6.2M18.8 6.5a7.8 7.8 0 0 1 0 11"/>`,
  ),
  volumeOff: svg(
    `<path d="M4 9.6v4.8c0 .7.5 1.2 1.2 1.2h2.5l4.1 3.5c.7.6 1.7.1 1.7-.8V5.7c0-.9-1-1.4-1.7-.8L7.7 8.4H5.2C4.5 8.4 4 8.9 4 9.6z"/><path ${STROKE} d="M16.6 9.6l4.8 4.8M21.4 9.6l-4.8 4.8"/>`,
  ),
  like: svg(`<path ${STROKE} d="${THUMB}"/>`),
  liked: svg(`<path d="${THUMB}" fill="#000" stroke="#000" stroke-width="1.7" stroke-linejoin="round"/>`),
  dislike: svg(`<g transform="rotate(180 12 12)"><path ${STROKE} d="${THUMB}"/></g>`),
  disliked: svg(
    `<g transform="rotate(180 12 12)"><path d="${THUMB}" fill="#000" stroke="#000" stroke-width="1.7" stroke-linejoin="round"/></g>`,
  ),
  expand: svg(`<path ${STROKE} d="M6 14.6 12 8.6l6 6"/>`),
  collapse: svg(`<path ${STROKE} d="M6 9.4 12 15.4l6-6"/>`),

  // ── sesl-androidx 원본 경로 ──
  more: svg(
    '<path fill-rule="evenodd" d="M12,16.75C12.868,16.75 13.5,17.382 13.5,18.25C13.5,19.118 12.868,19.75 12,19.75C11.211,19.75 10.5,19.118 10.5,18.25C10.5,17.382 11.132,16.75 12,16.75ZM12,10.5C12.868,10.5 13.5,11.132 13.5,12C13.5,12.868 12.868,13.5 12,13.5C11.211,13.5 10.5,12.868 10.5,12C10.5,11.132 11.132,10.5 12,10.5ZM12,4.25C12.789,4.25 13.5,4.882 13.5,5.75C13.5,6.618 12.868,7.25 12,7.25C11.211,7.25 10.5,6.618 10.5,5.75C10.5,4.882 11.132,4.25 12,4.25Z"/>',
  ),
  search: svg(
    '<path fill-rule="evenodd" d="M10.625,5C7.518,5 5,7.519 5,10.625C5,13.731 7.518,16.25 10.625,16.25C13.732,16.25 16.25,13.731 16.25,10.625C16.25,7.519 13.732,5 10.625,5ZM3.5,10.625C3.5,6.69 6.69,3.5 10.625,3.5C14.56,3.5 17.75,6.69 17.75,10.625C17.75,12.322 17.156,13.881 16.166,15.105L20.705,19.645C20.998,19.937 20.998,20.412 20.705,20.705C20.412,20.998 19.937,20.998 19.645,20.705L15.105,16.165C13.881,17.156 12.322,17.75 10.625,17.75C6.69,17.75 3.5,14.56 3.5,10.625Z"/>',
  ),
  clear: svg(
    '<path d="M13.06,11.999L19.031,6.03C19.324,5.737 19.324,5.263 19.031,4.97C18.738,4.677 18.262,4.677 17.97,4.97L11.999,10.938L6.03,4.97C5.737,4.677 5.262,4.677 4.969,4.97C4.676,5.263 4.676,5.737 4.969,6.03L10.939,11.999L4.969,17.97C4.676,18.263 4.676,18.737 4.969,19.03C5.116,19.177 5.307,19.249 5.499,19.249C5.692,19.249 5.883,19.177 6.03,19.03L11.999,13.061L17.97,19.03C18.116,19.177 18.308,19.249 18.5,19.249C18.692,19.249 18.884,19.177 19.031,19.03C19.324,18.737 19.324,18.263 19.031,17.97L13.06,11.999Z"/>',
  ),
  headphones: svg(
    `<path ${STROKE} d="M4.6 14.6v-2.4a7.4 7.4 0 0 1 14.8 0v2.4"/><rect x="3.6" y="13.4" width="4.6" height="7" rx="2"/><rect x="15.8" y="13.4" width="4.6" height="7" rx="2"/>`,
  ),
  video: svg(
    `<rect ${STROKE} x="3.4" y="5.4" width="17.2" height="13.2" rx="3"/><path d="M10.2 9.3v5.4c0 .6.6.9 1.1.6l4.2-2.7c.4-.3.4-.9 0-1.2l-4.2-2.7c-.5-.3-1.1 0-1.1.6z"/>`,
  ),
  drawer: svg(`<path ${STROKE} d="M4.5 7h15M4.5 12h15M4.5 17h15"/>`),

  // ── 사이드바 (선택되면 채워진 아이콘) ──
  home: svg(
    `<path ${STROKE} d="M4.2 10.3 11.2 4.7c.5-.4 1.1-.4 1.6 0l7 5.6c.3.2.4.6.4 1v7.9c0 .8-.6 1.4-1.4 1.4h-3.6v-5.1c0-.6-.4-1-1-1h-4.4c-.6 0-1 .4-1 1v5.1H5.2c-.8 0-1.4-.6-1.4-1.4v-7.9c0-.4.1-.8.4-1z"/>`,
  ),
  homeFilled: svg(
    '<path d="M3.4 10.3 10.8 4.4c.7-.6 1.7-.6 2.4 0l7.4 5.9c.4.3.6.8.6 1.3v7.6c0 1.2-1 2.1-2.1 2.1h-3.7c-.4 0-.8-.4-.8-.8v-4.7c0-.6-.4-1-1-1h-3.2c-.6 0-1 .4-1 1v4.7c0 .4-.4.8-.8.8H4.9c-1.2 0-2.1-1-2.1-2.1v-7.6c0-.5.2-1 .6-1.3z"/>',
  ),
  explore: svg(
    `<circle ${STROKE} cx="12" cy="12" r="8.4"/><path d="M15.4 8.6l-1.8 4.6c-.1.2-.2.3-.4.4l-4.6 1.8 1.8-4.6c.1-.2.2-.3.4-.4z" stroke="#000" stroke-width="1.2" stroke-linejoin="round"/>`,
  ),
  exploreFilled: svg(
    '<path fill-rule="evenodd" d="M12 2.8a9.2 9.2 0 1 0 0 18.4 9.2 9.2 0 0 0 0-18.4zm3.4 5.8-1.8 4.6c-.1.2-.2.3-.4.4l-4.6 1.8 1.8-4.6c.1-.2.2-.3.4-.4z"/>',
  ),
  library: svg(
    `<rect ${STROKE} x="4" y="4.4" width="4.2" height="15.2" rx="1.2"/><rect ${STROKE} x="10" y="4.4" width="4.2" height="15.2" rx="1.2"/><path ${STROKE} d="M16.4 5.6l2.3-.7c.6-.2 1.3.2 1.4.8l3 12.3c.1.6-.2 1.2-.8 1.4l-2.3.7c-.6.2-1.3-.2-1.4-.8l-3-12.3c-.1-.6.2-1.2.8-1.4z" transform="translate(-1.6 0)"/>`,
  ),
  libraryFilled: svg(
    '<rect x="3.2" y="3.6" width="5.6" height="16.8" rx="1.6"/><rect x="9.6" y="3.6" width="5.6" height="16.8" rx="1.6"/><path d="M15.2 5.4l2.6-.8c.8-.2 1.7.3 1.9 1.1l3 12.4c.2.8-.3 1.7-1.1 1.9l-2.6.8c-.8.2-1.7-.3-1.9-1.1l-3-12.4c-.2-.8.3-1.7 1.1-1.9z" transform="translate(-1.6 0)"/>',
  ),
} as const;

export type IconName = keyof typeof ICONS;

const urlCache = new Map<IconName, string>();
const iconUrl = (name: IconName) => {
  let url = urlCache.get(name);
  if (!url) {
    url = `url("data:image/svg+xml,${encodeURIComponent(ICONS[name])}")`;
    urlCache.set(name, url);
  }
  return url;
};

/** 아이콘 칸 하나에 One UI 아이콘을 씌운다 (이미 같은 아이콘이면 건드리지 않는다) */
const setIcon = (el: Element | null | undefined, name: IconName, size?: string) => {
  if (!(el instanceof HTMLElement)) return;
  if (el.dataset.oneuiIcon === name) return;
  el.dataset.oneuiIcon = name;
  el.style.setProperty('--oneui-ic', iconUrl(name));
  if (size) el.style.setProperty('--oneui-ic-size', size);
};

const GUIDE = [
  ['home', 'homeFilled'],
  ['explore', 'exploreFilled'],
  ['library', 'libraryFilled'],
] as const;

/** 화면을 훑어 아이콘을 바꾼다 — 상태(재생 · 반복 · 좋아요 · 선택)에 따라 아이콘이 달라진다 */
export const applyIcons = () => {
  tagShelves();
  // 재생 바 아이콘(재생 · 이전 · 다음 · 셔플 · 반복 · 볼륨 · 열기)은 bar-icons.ts 가 모양 변환 SVG 로 맡는다

  // ⋮ 더보기 (목록 줄 · 카드 · 재생 바 등 모든 메뉴 버튼)
  document
    .querySelectorAll('ytmusic-menu-renderer > yt-button-shape .ytSpecButtonShapeNextIcon')
    .forEach((el) => setIcon(el, 'more'));

  // 상단: 서랍(≡) · 검색
  setIcon(document.querySelector('ytmusic-nav-bar #guide-icon'), 'drawer');
  setIcon(document.querySelector('ytmusic-search-box .search-button yt-icon'), 'search', '22px');
  setIcon(document.querySelector('ytmusic-search-box #clear-button yt-icon'), 'clear', '20px');

  // 사이드바 첫 묶음: 홈 · 둘러보기 · 보관함
  for (const root of document.querySelectorAll('#mini-guide, #guide-renderer, #guide-content')) {
    const section = root.querySelector('ytmusic-guide-section-renderer');
    const entries = section?.querySelectorAll('ytmusic-guide-entry-renderer') ?? [];
    entries.forEach((entry, i) => {
      const pair = GUIDE[i];
      if (!pair) return;
      setIcon(entry.querySelector('yt-icon.guide-icon'), entry.hasAttribute('active') ? pair[1] : pair[0]);
    });
  }
};

/** '다시 듣기' 셸프는 2행 그리드로 (CSS: .oneui-two-rows) */
const TWO_ROW_TITLES = ['다시 듣기', 'Listen again'];
const tagShelves = () => {
  document.querySelectorAll('ytmusic-carousel-shelf-renderer').forEach((shelf) => {
    const title = shelf
      .querySelector('ytmusic-carousel-shelf-basic-header-renderer .title')
      ?.textContent?.trim();
    shelf.classList.toggle('oneui-two-rows', !!title && TWO_ROW_TITLES.includes(title));
  });
};

/** 아이콘을 지운다 (테마를 끌 때) */
export const clearIcons = () => {
  document.querySelectorAll<HTMLElement>('[data-oneui-icon]').forEach((el) => {
    delete el.dataset.oneuiIcon;
    el.style.removeProperty('--oneui-ic');
    el.style.removeProperty('--oneui-ic-size');
  });
};

/** CSS 가 ::before 로 그리는 아이콘들 (노래/동영상 전환 등) — :root 변수로 내보낸다 */
const ROOT_ICONS: IconName[] = ['headphones', 'video'];

/** 화면이 바뀔 때마다(재생 · 페이지 이동 · 목록 추가) 다시 훑는다 */
export const startIcons = () => {
  ROOT_ICONS.forEach((name) =>
    document.documentElement.style.setProperty(`--oneui-ic-${name}`, iconUrl(name)),
  );
  // 변화가 몰려 와도 150ms 에 한 번만 훑는다
  let scheduled = 0;
  const schedule = () => {
    if (scheduled) return;
    scheduled = window.setTimeout(() => {
      scheduled = 0;
      applyIcons();
    }, 150);
  };
  const observer = new MutationObserver(schedule);
  observer.observe(document.body, {
    subtree: true,
    childList: true,
    attributes: true,
    // YT 가 매초 바꾸는 aria-label · title 은 지켜보지 않는다
    attributeFilter: ['active', 'repeat-mode'],
  });
  const media = ['play', 'pause', 'ended', 'volumechange', 'loadeddata'];
  const onMedia = (e: Event) => {
    if (e.target instanceof HTMLVideoElement) schedule();
  };
  media.forEach((ev) => document.addEventListener(ev, onMedia, true));
  applyIcons();
  return () => {
    observer.disconnect();
    media.forEach((ev) => document.removeEventListener(ev, onMedia, true));
    clearTimeout(scheduled);
    clearIcons();
    document.querySelectorAll('.oneui-two-rows').forEach((el) => el.classList.remove('oneui-two-rows'));
    ROOT_ICONS.forEach((name) => document.documentElement.style.removeProperty(`--oneui-ic-${name}`));
  };
};
