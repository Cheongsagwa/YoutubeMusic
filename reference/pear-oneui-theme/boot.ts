/**
 * 앱을 켤 때 원래 YouTube Music 모습이 잠깐 보였다가 One UI 로 바뀌던 것을 막는다.
 *
 * 테마 플러그인은 페이지가 다 뜬 뒤에야 시작되므로, 그 전까지는 preload(페이지보다 먼저 실행)에서
 * 화면을 One UI 배경색으로 덮어 둔다. 테마가 붙으면(<html class="oneui-theme">) 덮개가 풀리며 부드럽게 나타난다.
 * 혹시 테마가 끝내 붙지 못해도 6초 뒤에는 원래 화면이 보이게 한다.
 */
import type { WebFrame } from 'electron';

const BOOT_CSS = `
html:not(.oneui-theme) { background: #17171a !important; }
html:not(.oneui-theme) body { background: #17171a !important; }
html:not(.oneui-theme) body > * {
  opacity: 0 !important;
  animation: oneui-boot-reveal 0s linear 6s forwards !important;
}
@keyframes oneui-boot-reveal { to { opacity: 1; } }
@keyframes oneui-boot-in { from { opacity: 0; } to { opacity: 1; } }
html.oneui-theme body > ytmusic-app { animation: oneui-boot-in 380ms ease-out; }
`;

type Ipc = { on: (channel: string, listener: (event: unknown, id: string) => void) => void };

export const installBootCover = (enabled: boolean, webFrame: WebFrame, ipc: Ipc) => {
  if (!enabled) return;
  const key = webFrame.insertCSS(BOOT_CSS);
  // 실행 중에 테마를 끄면 덮개도 뗀다 (안 그러면 화면이 가려진다)
  ipc.on('plugin:unload', (_event, id) => {
    if (id === 'oneui-theme') webFrame.removeInsertedCSS(key);
  });
};
