import Color from 'color';
import { FastAverageColor } from 'fast-average-color';

/**
 * 삼성 뮤직 잠금화면 같은 파도 재생 바.
 *
 * - 직선은 항상 그대로 (재생한 쪽 = 앨범 색, 남은 쪽 = 회색)
 * - 재생한 구간 위로 반투명한 언덕(파도)들이 솟아 손잡이 쪽으로 계속 흘러간다
 * - 언덕마다 높이 · 폭 · 속도가 제각각이고, 흘러가는 동안 높이가 살짝 숨 쉬듯 변한다
 * - 멈추면 파도가 가라앉아 직선만 남는다
 * - 색은 앨범아트의 대표색 (어두운 배경에서 잘 보이게 밝기 · 채도 보정)
 *
 * YouTube Music 의 진행 막대(tp-yt-paper-slider)는 그대로 두고 막대 색만 투명하게 만든 뒤,
 * 그 뒤에 캔버스를 깔아 그린다. 그래서 클릭 · 드래그로 넘기기는 원래대로 동작한다.
 */

/** 파도 모양 — 숫자만 바꾸면 느낌이 바뀐다 */
const WAVE = {
  /** 직선 굵기 (px) */
  lineWidth: 4,
  /** 직선 위로 솟는 파도의 최대 높이 (px) */
  height: 13,
  /** 솟아오르는 시간 · 멈췄을 때 가라앉는 시간 (초) */
  rise: 0.45,
  fall: 1.1,
  /** 손잡이 반지름 · 흰 테두리 두께 (px) */
  knobRadius: 9,
  knobRing: 2,
  /** 시작점 · 손잡이 근처에서 파도가 0 으로 모이는 구간 (px) */
  edgeFade: 40,
  /**
   * 겹쳐 그리는 파도 층 (뒤에서 앞 순서).
   * count: 층 안의 언덕 수, speed: 언덕마다 고르는 속도 범위(px/초),
   * width: 언덕 폭 범위(px), peak: 언덕 높이 범위(최대 높이 대비 비율)
   */
  layers: [
    { tint: 'grey', alpha: [0.1, 0.28], count: 7, speed: [45, 85], width: [40, 80], peak: [0.55, 1.0] },
    { tint: 'accent', alpha: [0.2, 0.5], count: 8, speed: [55, 105], width: [30, 65], peak: [0.45, 0.95] },
    { tint: 'accent', alpha: [0.45, 0.85], count: 8, speed: [65, 125], width: [22, 48], peak: [0.3, 0.7] },
  ],
} as const;

const FALLBACK_COLOR = '#e5e5ea';
const COLOR_VAR = '--oneui-wave-color';
/** 앱 전체 배경 · 유리 표면이 쓰는 앨범 색 (style.css 에서 @property 로 부드럽게 바뀐다) */
const ALBUM_VAR = '--oneui-album';
const WAVE_CLASS = 'oneui-wave';

type Slider = HTMLElement & {
  dragging?: boolean;
  value?: number;
  immediateValue?: number;
  max?: number;
};

type Range = readonly [number, number];
type Layer = (typeof WAVE.layers)[number];

/** 흘러가는 언덕 하나 */
type Bump = {
  /** 가운데 위치 — 직선 시작점 기준 px */
  x: number;
  speed: number;
  width: number;
  peak: number;
  /** 숨쉬기(높이 흔들림) 속도 · 위상 */
  wobble: number;
  phase: number;
  /** 언덕마다 다른 진하기 */
  alpha: number;
};

const rand = ([a, b]: Range) => a + Math.random() * (b - a);
const smooth = (t: number) => t * t * (3 - 2 * t);

/** 앨범 색을 어두운 배경 위 강조색으로 다듬는다 */
const toAccent = (hex: string) => {
  const [h, s, l] = Color(hex).hsl().array() as [number, number, number];
  // 거의 회색인 앨범은 회색 계열을 유지하고, 색이 있으면 채도를 올린다
  // 쨍하게: 채도는 85 이상, 밝기는 너무 하얗게 뜨지 않도록 52 ~ 62
  const sat = s < 12 ? s : Math.max(s, 85);
  const light = Math.min(Math.max(l, 52), 62);
  return Color.hsl(h, sat, light).hex();
};

export class SeekWave {
  private canvas: HTMLCanvasElement | null = null;
  private ctx: CanvasRenderingContext2D | null = null;
  private frame = 0;
  private last = 0;
  /** 0 = 직선, 1 = 파도 */
  private amp = 0;
  private color = FALLBACK_COLOR;
  private rgb = '229, 229, 234';
  /** 흐름 속도 배율 — 멈추면 1 → 0 으로 부드럽게 줄어 파도가 그 자리에서 굳은 채 가라앉는다 */
  private flow = 0;
  private lastDrawKey = '';
  private fac = new FastAverageColor();
  private colorJob = 0;
  /** 캔버스 안 직선의 y · 트랙 시작 x (손잡이가 삐져나갈 여백) */
  private baseline = 0;
  private padX = 0;
  /** 마지막으로 그린 시각 — 고주사율 화면(240Hz)에서도 60fps 로만 그린다 */
  private lastDraw = 0;
  /** 레이아웃 측정값 캐시 (매 프레임 getBoundingClientRect 를 부르면 강제 레이아웃이 일어난다) */
  private layout: {
    at: number;
    barLeft: number;
    barTop: number;
    trackLeft: number;
    trackWidth: number;
    centerY: number;
  } | null = null;
  private styleKey = '';
  /** 층마다 흘러가는 언덕들 */
  private bumps: Bump[][] = WAVE.layers.map(() => []);

  attach() {
    if (this.canvas) return;
    this.canvas = document.createElement('canvas');
    this.canvas.className = 'oneui-wave-canvas';
    this.ctx = this.canvas.getContext('2d');
    document.documentElement.classList.add(WAVE_CLASS);
    this.applyColor(this.color);
    this.last = performance.now();
    this.frame = requestAnimationFrame(this.tick);
  }

  detach() {
    cancelAnimationFrame(this.frame);
    this.frame = 0;
    this.canvas?.remove();
    this.canvas = null;
    this.ctx = null;
    this.lastDrawKey = '';
    document.documentElement.classList.remove(WAVE_CLASS);
    // 앨범 색(--oneui-album · --oneui-wave-color)은 파도를 꺼도 배경이 계속 쓰므로 남겨 둔다
  }

  /** 테마를 완전히 끌 때 */
  clearColor() {
    document.documentElement.style.removeProperty(COLOR_VAR);
    document.documentElement.style.removeProperty(ALBUM_VAR);
  }

  /** 곡이 바뀌면 썸네일 주소로 불러 색을 뽑는다 */
  async updateColorFrom(url: string | undefined) {
    const job = ++this.colorJob;
    if (!url) {
      this.applyColor(FALLBACK_COLOR);
      return;
    }
    try {
      const result = await this.fac.getColorAsync(url, {
        algorithm: 'dominant',
        crossOrigin: 'anonymous',
      });
      if (job !== this.colorJob) return; // 그 사이 다른 곡으로 넘어감
      this.applyColor(toAccent(result.hex));
    } catch (err) {
      console.warn('[oneui-theme] 앨범 색을 뽑지 못했습니다', err);
      if (job === this.colorJob) this.applyColor(FALLBACK_COLOR);
    }
  }

  private applyColor(color: string) {
    this.color = color;
    this.rgb = Color(color).rgb().array().slice(0, 3).map(Math.round).join(', ');
    this.lastDrawKey = '';
    document.documentElement.style.setProperty(COLOR_VAR, color);
    document.documentElement.style.setProperty(ALBUM_VAR, color);
  }

  /** 언덕 하나를 새로 만든다. start 면 직선 왼쪽 바깥에서 태어난다 */
  private spawn(layer: Layer, length: number, start: boolean): Bump {
    const width = rand(layer.width);
    return {
      x: start ? -width * rand([0.5, 1.5]) : Math.random() * length,
      speed: rand(layer.speed),
      width,
      peak: rand(layer.peak),
      wobble: rand([1.4, 3.6]),
      phase: Math.random() * Math.PI * 2,
      alpha: rand(layer.alpha),
    };
  }

  /** 언덕들을 앞으로 흘려 보내고, 손잡이를 지나면 왼쪽에서 다시 태어나게 한다 */
  private advance(dt: number, length: number) {
    WAVE.layers.forEach((layer, i) => {
      const list = this.bumps[i];
      // 재생한 구간이 길수록 언덕도 많게 (잔잔한 빈 구간이 생기지 않도록)
      const want = Math.max(2, Math.round(layer.count * Math.max(0.5, length / 420)));
      while (list.length < want) list.push(this.spawn(layer, length, false));
      if (list.length > want + 2) list.length = want;
      for (let j = 0; j < list.length; j++) {
        const b = list[j];
        b.x += b.speed * dt;
        b.phase += b.wobble * dt;
        if (b.x - b.width > length) list[j] = this.spawn(layer, length, true);
      }
    });
  }

  private tick = (now: number) => {
    this.frame = requestAnimationFrame(this.tick);
    // 60fps 로 제한 (240Hz 화면이면 4프레임에 한 번)
    if (now - this.lastDraw < 1000 / 60 - 1) return;
    if (document.hidden) return;
    const dt = Math.min((now - this.last) / 1000, 0.1);
    this.last = now;
    this.lastDraw = now;

    const bar = document.querySelector<HTMLElement>('ytmusic-player-bar');
    const slider = bar?.querySelector<Slider>('#progress-bar');
    const canvas = this.canvas;
    const ctx = this.ctx;
    if (!bar || !slider || !canvas || !ctx) return;

    // 캔버스가 진행 막대 뒤에 오도록 플레이어 바 맨 앞에 넣는다
    if (canvas.parentElement !== bar) bar.insertBefore(canvas, bar.firstChild);

    const video = document.querySelector<HTMLVideoElement>('video');
    const playing = !!video && !video.paused && !video.ended;

    // 솟아오르기 / 가라앉기
    const target = playing ? 1 : 0;
    const k = 1 - Math.exp(-dt / ((playing ? WAVE.rise : WAVE.fall) / 3));
    this.amp += (target - this.amp) * k;
    this.flow += ((playing ? 1 : 0) - this.flow) * (1 - Math.exp(-dt / 0.18));
    if (Math.abs(target - this.amp) < 0.002) this.amp = target;

    // 위치 측정은 0.3초에 한 번만 (창 크기가 바뀌어도 곧 따라온다)
    if (!this.layout || now - this.layout.at > 300) {
      const barRect = bar.getBoundingClientRect();
      const trackEl =
        slider.querySelector<HTMLElement>('#sliderBar') ??
        slider.querySelector<HTMLElement>('.bar-container') ??
        slider;
      const track = trackEl.getBoundingClientRect();
      const sliderRect = slider.getBoundingClientRect();
      this.layout = {
        at: now,
        barLeft: barRect.left,
        barTop: barRect.top,
        trackLeft: track.left,
        trackWidth: track.width,
        centerY:
          (track.height > 0
            ? track.top + track.height / 2
            : sliderRect.top + sliderRect.height / 2) - barRect.top,
      };
    }
    const L = this.layout;
    const width = Math.round(L.trackWidth);
    if (width <= 0) return;

    // 선 끝 = 손잡이 위치.
    // YT 는 막대 값을 1초에 한 번만 바꿔서 뚝뚝 끊기므로 YT 플레이어 API 의 시간으로 매번 계산한다.
    // (자동으로 다음 곡이 이어질 때 YT 는 같은 <video> 에 이어 붙여 재생해서 video.currentTime 은 0 으로 돌아가지 않는다)
    // 곡 길이는 YT 진행 막대의 최댓값 — 곡이 막 바뀐 직후 API 의 getDuration() 은 20 · 30 초처럼 틀린 값을 준다.
    const dragging = !!slider.dragging || slider.hasAttribute('dragging');
    const api = document.querySelector('#movie_player') as
      | (HTMLElement & { getCurrentTime?: () => number; getDuration?: () => number })
      | null;
    const current = api?.getCurrentTime?.() ?? video?.currentTime ?? 0;
    const sliderMax = Number(slider.max ?? slider.getAttribute('aria-valuemax') ?? 0);
    const duration =
      sliderMax > 0 ? sliderMax : (api?.getDuration?.() ?? video?.duration ?? 0);
    let end: number;
    if (dragging) {
      const knob = slider.querySelector<HTMLElement>('#sliderKnob')?.getBoundingClientRect();
      end = knob && knob.width > 0 ? knob.left + knob.width / 2 - L.trackLeft : 0;
    } else {
      end = Number.isFinite(duration) && duration > 0 ? (current / duration) * width : 0;
    }
    end = Math.min(Math.max(end, 0), width);

    // 캔버스 크기 · 위치 (바뀔 때만 스타일을 쓴다)
    const knobSpace = WAVE.knobRadius + 8;
    const below = Math.max(WAVE.lineWidth / 2 + 2, knobSpace);
    const above = Math.max(WAVE.height + WAVE.lineWidth / 2 + 2, knobSpace);
    const height = Math.round(above + below);
    const padX = knobSpace;
    const fullWidth = width + padX * 2;
    const dpr = window.devicePixelRatio || 1;
    const left = Math.round(L.trackLeft - L.barLeft - padX);
    const top = Math.round(L.centerY - above);
    const styleKey = `${left}|${top}|${fullWidth}|${height}`;
    if (styleKey !== this.styleKey) {
      this.styleKey = styleKey;
      canvas.style.left = `${left}px`;
      canvas.style.top = `${top}px`;
      canvas.style.width = `${fullWidth}px`;
      canvas.style.height = `${height}px`;
    }
    this.baseline = above;
    this.padX = padX;
    if (canvas.width !== Math.round(fullWidth * dpr) || canvas.height !== Math.round(height * dpr)) {
      canvas.width = Math.round(fullWidth * dpr);
      canvas.height = Math.round(height * dpr);
      this.lastDrawKey = '';
    }

    if (this.amp > 0) this.advance(dt * this.flow, end);

    // 멈춰 있고 바뀐 게 없으면 다시 그리지 않는다
    const key = `${width}|${height}|${end.toFixed(1)}|${this.amp.toFixed(3)}|${this.color}`;
    if (this.amp === 0 && key === this.lastDrawKey) return;
    this.lastDrawKey = key;

    this.draw(ctx, width, fullWidth, height, dpr, end);
  };

  private draw(
    ctx: CanvasRenderingContext2D,
    width: number,
    fullWidth: number,
    height: number,
    dpr: number,
    end: number,
  ) {
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, fullWidth, height);
    // 이후 좌표는 트랙 시작점 기준
    ctx.translate(this.padX, 0);

    const base = this.baseline;
    const half = WAVE.lineWidth / 2;
    const rise = smooth(this.amp);

    // ① 직선 위로 솟는 파도 (재생한 구간에만)
    if (rise > 0 && end > 6) {
      const fade = Math.min(WAVE.edgeFade, end / 2);
      // 언덕마다 따로 그린다 — 진하기가 제각각이라 겹치는 곳은 더 진해지고 연한 언덕도 섞인다
      WAVE.layers.forEach((layer, i) => {
        for (const b of this.bumps[i]) {
          const from = Math.max(0, b.x - b.width);
          const to = Math.min(end, b.x + b.width);
          if (to <= from) continue;
          const breathe = 0.4 + 0.6 * (0.5 + 0.5 * Math.sin(b.phase)); // 높이가 계속 오르내린다
          ctx.fillStyle =
            layer.tint === 'grey' ? `rgba(255, 255, 255, ${b.alpha})` : `rgba(${this.rgb}, ${b.alpha})`;
          ctx.beginPath();
          ctx.moveTo(from, base);
          for (let x = from; x <= to + 0.01; x += 2) {
            const d = Math.abs(x - b.x) / b.width;
            const shape = 1 - smooth(Math.min(1, d)); // 가운데가 뾰족한 /\
            const edge = smooth(Math.min(1, x / fade, (end - x) / fade));
            ctx.lineTo(x, base - WAVE.height * rise * edge * b.peak * breathe * shape);
          }
          ctx.lineTo(to, base);
          ctx.closePath();
          ctx.fill();
        }
      });
    }

    // ② 직선 (항상 그대로) — 재생한 쪽은 앨범 색, 남은 쪽은 회색. 손잡이 중심까지 이어진다
    ctx.lineCap = 'round';
    ctx.lineWidth = WAVE.lineWidth;
    if (end < width - half) {
      ctx.strokeStyle = 'rgba(255, 255, 255, 0.22)';
      ctx.beginPath();
      ctx.moveTo(Math.max(end, half), base);
      ctx.lineTo(width - half, base);
      ctx.stroke();
    }
    if (end > half) {
      ctx.strokeStyle = this.color;
      ctx.beginPath();
      ctx.moveTo(half, base);
      ctx.lineTo(end, base);
      ctx.stroke();
    }

    // ③ 손잡이 — 앨범 색 + 흰 테두리 + 같은 색 빛 (YT 손잡이는 투명하게 숨겨 둠)
    const r = WAVE.knobRadius;
    ctx.save();
    ctx.shadowColor = this.color;
    ctx.shadowBlur = 10;
    ctx.fillStyle = '#ffffff';
    ctx.beginPath();
    ctx.arc(end, base, r, 0, Math.PI * 2);
    ctx.fill();
    ctx.restore();
    ctx.fillStyle = this.color;
    ctx.beginPath();
    ctx.arc(end, base, r - WAVE.knobRing, 0, Math.PI * 2);
    ctx.fill();
  }
}
