/**
 * 플로팅 재생 바 보강.
 *
 * 1) 지금 부르는 가사 한 줄을 ⋮ 버튼 옆에 띄운다 (실시간 가사 플러그인이 받아 둔 가사를 읽는다.
 *    시간 정보가 있는 가사가 없으면 아무것도 띄우지 않는다)
 * 2) 가수 줄은 '가수'만 남긴다 (앨범 · 연도 빼기)
 * 3) 좋아요 · 싫어요는 바에서 숨기고, 재생 바의 ⋮ 메뉴 맨 위에 항목으로 넣는다
 */
import type { LyricResult } from '@/plugins/synced-lyrics/types';

import { marquee, stopMarquee } from './marquee';

/** 실시간 가사 플러그인이 globalThis 에 걸어 둔 지금 가사 (synced-lyrics/renderer/store.ts) */
const currentLyrics = () =>
  (
    globalThis as unknown as {
      ytmdSyncedLyrics?: { currentLyrics: () => { data: LyricResult | null } | undefined };
    }
  ).ytmdSyncedLyrics?.currentLyrics?.();

type Api = HTMLElement & { getCurrentTime?: () => number };

const LIKE_SVG =
  '<svg viewBox="0 0 24 24"><path fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" d="M7.6 10.6v8.9M7.6 10.6l3.5-6.1c.3-.6 1-.9 1.6-.7 1 .3 1.6 1.3 1.4 2.3l-.6 3.2h4.3c1.3 0 2.3 1.2 2 2.5l-1.4 5.8c-.2 1-1.1 1.7-2.1 1.7H7.6M7.6 10.6H5.2c-.6 0-1.1.5-1.1 1.1v6.7c0 .6.5 1.1 1.1 1.1h2.4"/></svg>';

export const startPlayerExtras = () => {
  // ── 가사 한 줄 · 가수만 ──
  const lyric = document.createElement('div');
  lyric.className = 'oneui-bar-lyric';
  // 가사: 잘라 내는 상자(lyricText) 안에 흐르는 글자(lyricInner)
  const lyricText = document.createElement('span');
  lyricText.className = 'oneui-mq-box';
  const lyricInner = document.createElement('span');
  lyricInner.className = 'oneui-mq-text';
  lyricText.appendChild(lyricInner);
  lyric.appendChild(lyricText);
  // 가수: 마찬가지로 상자 + 글자
  const artist = document.createElement('span');
  artist.className = 'oneui-artist oneui-mq-box';
  const artistInner = document.createElement('span');
  artistInner.className = 'oneui-mq-text';
  artist.appendChild(artistInner);

  // ── 재생 목록 지금 곡: 이퀄라이저 막대 ──
  const eq = document.createElement('div');
  eq.className = 'oneui-eq';
  eq.innerHTML = '<i></i><i></i><i></i>';
  // 막대 높이는 직접 계산한다: 재생 중엔 막대마다 다른 주기의 사인파를 섞어 불규칙하게 오르내리고,
  // 멈추면 그 자리에서 점(3px)으로 부드럽게 내려앉는다. 다시 재생하면 점에서 부드럽게 솟아오른다.
  const bars = [...eq.children] as HTMLElement[];
  const waves = bars.map(() =>
    [0, 1, 2].map(() => ({ f: 0.6 + Math.random() * 2.2, p: Math.random() * Math.PI * 2, a: 0.25 + Math.random() * 0.5 })),
  );
  let eqPlaying = false;
  let eqAmp = 0;
  let eqFrame = 0;
  let eqLast = 0;
  let eqT = 0;
  const eqTick = (now: number) => {
    // 30fps 면 충분하다 (고주사율 화면에서 매 프레임 그리지 않게)
    if (eqLast && now - eqLast < 30) {
      eqFrame = requestAnimationFrame(eqTick);
      return;
    }
    const dt = Math.min(0.1, eqLast ? (now - eqLast) / 1000 : 0.016);
    eqLast = now;
    eqFrame = 0;
    if (!eq.isConnected) return;
    // 재생 중엔 0.25초, 멈추면 0.35초 감속으로 크기가 바뀐다
    eqAmp += ((eqPlaying ? 1 : 0) - eqAmp) * (1 - Math.exp(-dt / (eqPlaying ? 0.12 : 0.16)));
    if (!eqPlaying && eqAmp < 0.005) eqAmp = 0;
    eqT += dt * (0.4 + 0.6 * eqAmp); // 멈출 때는 움직임도 함께 느려진다
    bars.forEach((bar, i) => {
      let v = 0;
      let norm = 0;
      for (const w of waves[i]) {
        v += w.a * (0.5 + 0.5 * Math.sin(eqT * w.f * Math.PI * 2 + w.p));
        norm += w.a;
      }
      const h = 3 + 13 * eqAmp * (v / norm);
      bar.style.height = `${h.toFixed(2)}px`;
    });
    if (eqPlaying || eqAmp > 0) eqFrame = requestAnimationFrame(eqTick);
    else eqLast = 0;
  };

  let beforeSel: Element | null = null;
  const syncEq = () => {
    // 화면에 보이는 재생 목록의 지금 곡 (숨은 목록이 하나 더 있을 수 있다)
    const item = [...document.querySelectorAll<HTMLElement>('ytmusic-player-queue-item[selected]')].find(
      (el) => el.offsetWidth > 0,
    );
    const art = item?.querySelector('.left-items');
    if (!item || !art) {
      eq.remove();
      beforeSel?.classList.remove('oneui-before-selected');
      beforeSel = null;
      return;
    }
    if (eq.parentElement !== art) art.appendChild(eq);
    const video = document.querySelector('video');
    eqPlaying = !!video && !video.paused && !video.ended;
    if (!eqFrame && (eqPlaying || eqAmp > 0)) eqFrame = requestAnimationFrame(eqTick);

    // 지금 곡 바로 위 줄의 구분선도 숨긴다 (줄마다 감싸는 상자가 있어 CSS 만으로는 위 줄을 고르기 어렵다)
    const box = item.parentElement?.matches('#contents') ? item : item.closest('#contents > *');
    const prev = box?.previousElementSibling?.matches('ytmusic-player-queue-item')
      ? box.previousElementSibling
      : box?.previousElementSibling?.querySelector('ytmusic-player-queue-item') ?? null;
    if (prev !== beforeSel) {
      beforeSel?.classList.remove('oneui-before-selected');
      prev?.classList.add('oneui-before-selected');
      beforeSel = prev;
    }
  };

  // ── 재생 화면 탭(다음 트랙 · 가사 · 관련 항목 · 댓글) 내용이 부드럽게 바뀌도록 ──
  const onTabClick = (e: MouseEvent) => {
    const t = e.target instanceof Element ? e.target : null;
    const tab = t?.closest('ytmusic-player-page tp-yt-paper-tab');
    if (!tab || tab.classList.contains('iron-selected')) return;
    const content = document.querySelector<HTMLElement>('ytmusic-player-page #tab-renderer');
    if (!content) return;
    content.classList.remove('oneui-tab-in');
    void content.offsetWidth;
    content.classList.add('oneui-tab-in');
  };
  document.addEventListener('click', onTabClick, true);

  let shownLine = '';
  const tick = () => {
    syncEq();
    const bar = document.querySelector('ytmusic-player-bar');
    if (!bar) return;

    // 자리 잡기
    const buttons = bar.querySelector('.middle-controls-buttons');
    if (buttons && lyric.previousElementSibling !== buttons) buttons.after(lyric);
    const wrapper = bar.querySelector('.byline-wrapper');
    if (wrapper && artist.parentElement !== wrapper) wrapper.appendChild(artist);

    // 가수만
    const byline = bar.querySelector('.byline');
    const full = byline?.textContent ?? '';
    const name = full.split(' • ')[0]?.trim() ?? '';
    if (artistInner.textContent !== name) artistInner.textContent = name;

    // 지금 가사 줄
    const lines = currentLyrics()?.data?.lines;
    let line = '';
    if (Array.isArray(lines) && lines.length) {
      const api = document.querySelector<Api>('#movie_player');
      const ms = (api?.getCurrentTime?.() ?? 0) * 1000;
      for (let i = lines.length - 1; i >= 0; i--) {
        if (lines[i].timeInMs <= ms) {
          line = lines[i].text?.trim() ?? '';
          break;
        }
      }
    }
    lyric.classList.toggle('has-lyrics', Array.isArray(lines) && lines.length > 0);
    if (line !== shownLine) {
      shownLine = line;
      // 바뀔 때 살짝 떠오르며 바뀐다
      lyricText.classList.remove('in');
      void lyricText.offsetWidth;
      lyricInner.textContent = line || '♪';
      lyricText.classList.add('in');
    }

    // 길면 옆으로 흘려서 끝까지 보여 준다
    const title = bar.querySelector<HTMLElement>('.content-info-wrapper .title');
    title?.classList.add('oneui-mq-text');
    marquee(title, bar.querySelector<HTMLElement>('.content-info-wrapper'));
    marquee(artistInner, artist);
    if (lyric.classList.contains('has-lyrics')) marquee(lyricInner, lyricText);
  };
  const timer = window.setInterval(tick, 150);
  tick();

  // ── ⋮ 메뉴에 좋아요 · 싫어요 ──
  let fromBarAt = 0;
  const onClick = (e: MouseEvent) => {
    const t = e.target instanceof Element ? e.target : null;
    if (t?.closest('ytmusic-player-bar ytmusic-menu-renderer')) fromBarAt = performance.now();
  };

  const makeItem = (kind: 'like' | 'dislike') => {
    const bar = document.querySelector('ytmusic-player-bar');
    const status = bar?.querySelector('ytmusic-like-button-renderer')?.getAttribute('like-status');
    const active = (kind === 'like' && status === 'LIKE') || (kind === 'dislike' && status === 'DISLIKE');
    const item = document.createElement('div');
    item.className = `oneui-menu-item oneui-menu-${kind}${active ? ' active' : ''}`;
    item.setAttribute('role', 'menuitem');
    item.innerHTML = `<span class="icon">${LIKE_SVG}</span><span class="text"></span>`;
    item.querySelector('.text')!.textContent =
      kind === 'like' ? (active ? '좋아요 취소' : '좋아요') : active ? '싫어요 취소' : '싫어요';
    item.addEventListener('click', () => {
      bar?.querySelector<HTMLElement>(`ytmusic-like-button-renderer yt-button-shape.${kind} button`)?.click();
      // 메뉴 닫기
      (item.closest('tp-yt-iron-dropdown') as (HTMLElement & { close?: () => void }) | null)?.close?.();
    });
    return item;
  };

  const onOpened = (e: Event) => {
    if (performance.now() - fromBarAt > 1500) return;
    const dd = e.target instanceof HTMLElement ? e.target : null;
    const list = dd?.querySelector('ytmusic-menu-popup-renderer tp-yt-paper-listbox');
    if (!list) return;
    list.querySelectorAll('.oneui-menu-row, .oneui-menu-sep').forEach((el) => el.remove());
    // 한 줄에 두 칸 (좋아요 | 싫어요)
    const row = document.createElement('div');
    row.className = 'oneui-menu-row';
    row.append(makeItem('like'), makeItem('dislike'));
    const sep = document.createElement('div');
    sep.className = 'oneui-menu-sep';
    list.prepend(row, sep);
  };
  const onClosed = (e: Event) => {
    const dd = e.target instanceof HTMLElement ? e.target : null;
    // 닫힘 애니메이션이 끝난 뒤 지운다
    setTimeout(() => dd?.querySelectorAll('.oneui-menu-row, .oneui-menu-sep').forEach((el) => el.remove()), 260);
  };

  document.addEventListener('click', onClick, true);
  document.addEventListener('iron-overlay-opened', onOpened, true);
  document.addEventListener('iron-overlay-closed', onClosed, true);

  return () => {
    clearInterval(timer);
    cancelAnimationFrame(eqFrame);
    eq.remove();
    beforeSel?.classList.remove('oneui-before-selected');
    document.removeEventListener('click', onTabClick, true);
    stopMarquee(lyricInner);
    stopMarquee(artistInner);
    const title = document.querySelector<HTMLElement>('ytmusic-player-bar .content-info-wrapper .title');
    stopMarquee(title);
    title?.classList.remove('oneui-mq-text');
    lyric.remove();
    artist.remove();
    document.removeEventListener('click', onClick, true);
    document.removeEventListener('iron-overlay-opened', onOpened, true);
    document.removeEventListener('iron-overlay-closed', onClosed, true);
    document.querySelectorAll('.oneui-menu-row, .oneui-menu-sep').forEach((el) => el.remove());
  };
};
