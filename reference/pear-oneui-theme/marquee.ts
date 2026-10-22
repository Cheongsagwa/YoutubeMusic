/**
 * 긴 글자가 잘릴 때 버스 안 LED 처럼 옆으로 흘려서 끝까지 보여 준다.
 *
 * 잠깐 멈춤 → 끝까지 천천히 흐름 → 잠깐 멈춤 → 살짝 사라졌다가 처음으로 → 반복.
 * 글자 요소는 CSS 로 폭을 '내용만큼(max-content)' 으로 두고, 그 바깥 상자(clip)가 잘라 낸다.
 *
 * 양 끝 흐림(마스크)은 글자 요소 자신에게 건다 — 그래서 곡명이 흐를 때 가수 이름까지 흐려지지 않는다.
 * 글자가 왼쪽으로 d 만큼 움직이면 마스크는 오른쪽으로 d 만큼 옮겨서, 흐림은 상자 가장자리에 그대로 머문다.
 * 처음엔 오른쪽만 흐림 → 흐르기 시작하면 왼쪽도 → 끝에 닿으면 오른쪽은 사라짐 → 처음으로 돌아가면 다시 오른쪽만.
 */
const SPEED = 38; // px/초
const HOLD_START = 2200;
const HOLD_END = 1400;
const FADE = 280;
const EDGE = 22;

type State = { key: string; anim: Animation | null };
const states = new WeakMap<HTMLElement, State>();

let registered = false;
const registerProps = () => {
  if (registered) return;
  registered = true;
  for (const name of ['--oneui-mq-l', '--oneui-mq-r', '--oneui-mq-x']) {
    try {
      CSS.registerProperty({ name, syntax: '<length>', inherits: false, initialValue: '0px' });
    } catch {
      /* 이미 등록됨 */
    }
  }
};

/**
 * @param text  흘릴 글자 요소 (폭 = 내용만큼)
 * @param clip  글자가 보일 수 있는 바깥 상자
 * @param inset clip 안쪽에서 글자가 쓸 수 없는 폭 (좌우 여백 합)
 */
export const marquee = (text: HTMLElement | null, clip: HTMLElement | null, inset = 0) => {
  if (!text || !clip) return;
  registerProps();
  let st = states.get(text);
  if (!st) {
    st = { key: '', anim: null };
    states.set(text, st);
  }
  const avail = clip.clientWidth - inset;
  const need = text.offsetWidth;
  const key = `${text.textContent}|${avail}|${need}`;
  if (st.key === key) return;
  st.key = key;
  st.anim?.cancel();
  st.anim = null;

  const over = need - avail;
  text.classList.toggle('oneui-mq-over', over > 1);
  text.style.setProperty('--oneui-mq-w', `${Math.max(0, avail)}px`);
  if (over <= 1 || avail <= 0) return;

  const d = Math.ceil(over);
  const move = Math.max(900, (over / SPEED) * 1000);
  const total = HOLD_START + move + HOLD_END + FADE * 2;
  const at = (ms: number) => ms / total;
  const ramp = Math.min(260, move / 3);
  const on = `${EDGE}px`;
  const off = '0px';
  // 위치(translate)와 마스크 위치(--oneui-mq-x)는 항상 같은 양만큼 반대로 움직인다
  const pos = (px: number) => ({ translate: `${-px}px 0`, '--oneui-mq-x': `${px}px` });
  const ease = 'cubic-bezier(0.45, 0, 0.55, 1)';
  const moveAt = (k: number) => {
    // 이징이 걸린 이동 구간 중간 지점의 위치 (마스크 가장자리 변화 시점용)
    const t = k < 0.5 ? 2 * k * k : 1 - Math.pow(-2 * k + 2, 2) / 2;
    return d * t;
  };
  const r0 = ramp / move;
  st.anim = text.animate(
    [
      { ...pos(0), opacity: 1, '--oneui-mq-l': off, '--oneui-mq-r': on, offset: 0 },
      { ...pos(0), opacity: 1, '--oneui-mq-l': off, '--oneui-mq-r': on, offset: at(HOLD_START), easing: 'linear' },
      { ...pos(moveAt(r0)), opacity: 1, '--oneui-mq-l': on, '--oneui-mq-r': on, offset: at(HOLD_START + ramp), easing: ease },
      { ...pos(moveAt(1 - r0)), opacity: 1, '--oneui-mq-l': on, '--oneui-mq-r': on, offset: at(HOLD_START + move - ramp), easing: 'linear' },
      { ...pos(d), opacity: 1, '--oneui-mq-l': on, '--oneui-mq-r': off, offset: at(HOLD_START + move) },
      { ...pos(d), opacity: 1, '--oneui-mq-l': on, '--oneui-mq-r': off, offset: at(HOLD_START + move + HOLD_END) },
      { ...pos(d), opacity: 0, '--oneui-mq-l': on, '--oneui-mq-r': off, offset: at(HOLD_START + move + HOLD_END + FADE) },
      { ...pos(0), opacity: 0, '--oneui-mq-l': off, '--oneui-mq-r': on, offset: at(HOLD_START + move + HOLD_END + FADE) + 0.0001 },
      { ...pos(0), opacity: 1, '--oneui-mq-l': off, '--oneui-mq-r': on, offset: 1 },
    ] as Keyframe[],
    { duration: total, iterations: Infinity },
  );
};

export const stopMarquee = (text: HTMLElement | null) => {
  if (!text) return;
  states.get(text)?.anim?.cancel();
  states.delete(text);
  text.classList.remove('oneui-mq-over');
};
