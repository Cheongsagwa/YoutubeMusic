/**
 * 앨범아트가 바뀌거나 불러오는 동안 깨진 이미지 표시가 보이지 않게 하고,
 * 다 불러오면 페이드인한다. 깨진 이미지(불러오기 실패 · 빈 주소)는 아예 숨긴다.
 *
 * 재생 바 · 재생 화면 앨범아트는 마지막으로 보여 준 아트를 이미지 바로 밑 '받침'에 깔아 둔다.
 * 다음 곡으로 넘어가면 새 아트를 불러오는 동안에도 이전 아트가 그대로 보이고(회색 칸 없음),
 * 다 불러오면 새 아트가 가운데부터 부드럽게 번지듯 페이드인한다.
 * YT 가 이미지 주소만 바꾸든 이미지 요소를 새로 만들든 받침은 바깥 상자에 있어서 상관없다.
 */
const TARGETS = 'ytmusic-player-bar img, ytmusic-player-page #song-image img, ytmusic-player-queue-item img';
const ART = 'ytmusic-player-bar .thumbnail-image-wrapper img, ytmusic-player-page #song-image img';
const ART_BOX = '.thumbnail-image-wrapper, #song-image';
const REVEAL_MS = 700;

let registered = false;
const registerReveal = () => {
  if (registered) return;
  registered = true;
  try {
    CSS.registerProperty({ name: '--oneui-reveal', syntax: '<percentage>', inherits: false, initialValue: '0%' });
  } catch {
    /* 이미 등록됨 */
  }
};

export const startImageFade = () => {
  registerReveal();
  const timers = new WeakMap<HTMLImageElement, number>();

  /** 지금 보이는 아트를 받침에 깐다 (이미지와 같은 자리 · 같은 둥근 모서리) */
  const setUnder = (img: HTMLImageElement) => {
    const box = img.closest<HTMLElement>(ART_BOX);
    const src = img.currentSrc || img.src;
    if (!box || !/^https?:/.test(src)) return;
    let under = box.querySelector<HTMLElement>(':scope > .oneui-art-under');
    if (!under) {
      under = document.createElement('div');
      under.className = 'oneui-art-under';
      box.prepend(under);
      box.classList.add('oneui-art-box');
    }
    const b = box.getBoundingClientRect();
    const r = img.getBoundingClientRect();
    if (r.width === 0) return;
    under.style.left = `${r.left - b.left}px`;
    under.style.top = `${r.top - b.top}px`;
    under.style.width = `${r.width}px`;
    under.style.height = `${r.height}px`;
    under.style.borderRadius = getComputedStyle(img).borderRadius;
    under.style.backgroundImage = `url("${src.replace(/"/g, '%22')}")`;
  };

  /** 새 아트를 가운데부터 번지듯 드러내고, 끝나면 받침을 새 아트로 바꾼다 */
  const reveal = (img: HTMLImageElement) => {
    const box = img.closest(ART_BOX);
    const under = box?.querySelector<HTMLElement>(':scope > .oneui-art-under');
    const src = img.currentSrc || img.src;
    // 받침이 이미 같은 아트면(처음 표시 · 같은 그림 다시 불러오기) 바로 받침만 맞춘다
    if (!under || under.style.backgroundImage.includes(src)) {
      setUnder(img);
      return;
    }
    clearTimeout(timers.get(img));
    img.classList.remove('oneui-art-reveal');
    void img.offsetWidth;
    img.classList.add('oneui-art-reveal');
    timers.set(
      img,
      window.setTimeout(() => {
        img.classList.remove('oneui-art-reveal');
        setUnder(img);
      }, REVEAL_MS + 30),
    );
  };

  const loaded = (img: HTMLImageElement) => {
    img.classList.add('oneui-img-ok');
    if (img.matches(ART)) reveal(img);
  };
  const mark = (img: HTMLImageElement) => {
    const ok = img.complete && img.naturalWidth > 0 && !!img.getAttribute('src');
    if (ok) loaded(img);
    else img.classList.remove('oneui-img-ok');
  };
  const onLoad = (e: Event) => {
    const img = e.target;
    if (img instanceof HTMLImageElement && img.matches(TARGETS)) loaded(img);
  };
  const onError = (e: Event) => {
    const img = e.target;
    if (img instanceof HTMLImageElement) img.classList.remove('oneui-img-ok');
  };
  // 주소가 바뀌면 다시 숨겼다가(받침이 대신 보임) 불러오면 드러낸다
  const observer = new MutationObserver((list) => {
    for (const m of list) {
      const img = m.target;
      if (!(img instanceof HTMLImageElement) || !img.matches(TARGETS)) continue;
      img.classList.remove('oneui-img-ok');
      if (img.complete) mark(img);
    }
  });
  observer.observe(document.body, { subtree: true, attributes: true, attributeFilter: ['src'] });
  document.addEventListener('load', onLoad, true);
  document.addEventListener('error', onError, true);
  document.querySelectorAll<HTMLImageElement>(TARGETS).forEach(mark);
  document.documentElement.classList.add('oneui-img-fade');
  return () => {
    observer.disconnect();
    document.removeEventListener('load', onLoad, true);
    document.removeEventListener('error', onError, true);
    document.querySelectorAll('.oneui-art-under').forEach((el) => el.remove());
    document.querySelectorAll('.oneui-art-box').forEach((el) => el.classList.remove('oneui-art-box'));
    document.querySelectorAll('.oneui-art-reveal').forEach((el) => el.classList.remove('oneui-art-reveal'));
    document.documentElement.classList.remove('oneui-img-fade');
  };
};
