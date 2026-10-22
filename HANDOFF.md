# OneMusic — 인수인계 문서

YouTube Music 네이티브 윈도우 클라이언트. 원래 쓰던 Electron 앱(pear-desktop + 직접 만든 One UI 테마 플러그인)의
화면 · 동작을 그대로 옮기면서 메모리를 줄이는 게 목표. (측정: OneMusic 약 180MB / pear-desktop 약 2.6GB)
pear 테마 원본은 `reference/pear-oneui-theme/` (style.css 토큰 · bar-icons.ts · scroll-fade.ts …)

## 기술
- C# / .NET 8 / **WinUI 3** (Windows App SDK 1.6, 패키지 없는 실행 파일), Win2D(파도 재생 바 · 가사 · 흘리는 글자)
- UI 는 거의 전부 C# 코드로 만든다 (XAML 은 `App.xaml` 하나). 스타일 토큰은 `src/UI/Theme.cs`
- **윈도우에서만 빌드 · 실행된다.** ⚠️ 사용자 지시: **GitHub 에 올려서 Actions 로 빌드하지 말고, 사용자 PC 에서 빌드 · 실행 · 확인할 것** (push 금지)
  - 작업 폴더: `C:\Users\cheon\Desktop\OneMusic` (Cowork 에선 `$HOME/mnt/OneMusic`). 소스는 `src/`
  - PC 에서 `dev-watch.bat` 을 켜 두면 `.dev/q.sh build` · `.dev/q.sh run` 으로 빌드/실행, `.dev/aq.sh <명령>` 으로 실행 중인 앱에 명령 (아래 '개발용 통로')
  - 화면 확인은 컴퓨터 사용(스크린샷)으로 앱 창을 직접 본다 (앱 이름 resolve: exe 전체 경로)
  - GitHub 원격: https://github.com/Cheongsagwa/OneMusic (Actions 워크플로 `.github/workflows/build.yml` 은 있지만 지금은 안 씀)

## 구조 — Core
- `InnerTube.cs` — YouTube Music 내부 API(WEB_REMIX) 직접 호출. `Post(..., web: true)` 는 www.youtube.com(WEB) — 댓글용
  - 검색(맨 위 카드 버튼 · 갈래 · 칩) · 추천어 · 홈/둘러보기/보관함(칩 · 셸프 · 이어 받기) · 다음 트랙(라디오 · 칩 · automix) · 상세 화면 · 관련 항목
  - ⋮ 메뉴는 YT 가 주는 `menuRenderer` 를 그대로 파싱(`ParseMenu`) → `TrackMenu.RunNode` 가 엔드포인트별로 실행
  - 다음 트랙 한 칸에 음원(ATV)과 뮤비(OMV)가 같이 오면 **음원만 고르고** 뮤비는 `CounterpartId`(노래/동영상 전환)
  - 긴 목록(100곡 넘는 플레이리스트 · 보관함 노래 · 기록): `TrackToken` → `MoreTracks(token)` (새 방식 body continuation, 비면 ctoken)
  - 댓글: commentEntityPayload + toolbarSurface(좋아요/싫어요/답글 동작) + toolbarState(내가 누른 상태). `CommentAction` · `PostReply` · `PostComment`
  - 시간 가사: 가사 탭 browseId 를 iOS 앱(clientName 26) 으로 browse → timedLyricsData
- `TrackPager.cs` — 긴 목록 다음 묶음. 상세 화면(스크롤 끝 `MoreSentinel`)과 재생기(목록 끝 5곡 전)가 같은 것을 같이 쓴다
- `Auth.cs` + `UI/LoginWindow.cs` — **한국은 로그인 없이는 YT Music 응답이 안 온다**. WebView2 로그인 → youtube.com 쿠키를 DPAPI 로 암호화 저장 → SAPISIDHASH
- `StreamResolver.cs` — 스트림 주소는 **yt-dlp 폴더판**(`yt-dlp_win.zip` → %LocalAppData%\OneMusic\yt-dlp\, 실패하면 단일 exe).
  - 빠른 인자 `--extractor-args youtube:player_skip=webpage,configs;visitor_data=...` (약 2.5초), 실패하면 일반 인자로
  - 다음 곡 미리 받기 + 곡 줄에 0.35초 마우스 올리면 미리 받기(`HoverPrefetch`). 직접 player API(ANDROID_VR)는 첫 바이트가 3.8초 막혀서 꺼 둠(`UseDirectPlayerApi=false`)
  - 사흘 넘게 묵으면 뒤에서 `yt-dlp -U`, 주소 받기가 실패하면 한 번 업데이트 후 다시 (YouTube 가 바뀌어도 계속 재생되게)
  - 오디오는 2MB 조각 6개씩 동시에 받아 **통째로 메모리에** (32분짜리도 됨) → 복사 없이 `MemoryStream.AsRandomAccessStream()` 으로 재생기에. 최근 3곡만 들고 있음
  - 뮤직비디오는 `VideoManifest` 가 화면(avc1 mp4) + 소리(m4a) DASH 목록(MPD)을 만들어 AdaptiveMediaSource 로 흘려 받는다
    (소리+화면 mp4 = 18 은 403 으로 막힘, HLS 는 Windows 가 YouTube 소리 조각을 못 엶)
- `Mp4Fix.cs` — 일부 곡의 MP4 편집 목록(edts) 때문에 열자마자 끝나는 문제 → `free` 로 바꿔 무시
- `PlayerService.cs` — Windows `MediaPlayer` (미디어 키 · 시스템 미디어 컨트롤 자동)
  - 큐 = 고른 곡들 `Queue[..AutoStart]` + 자동재생 구간 `Queue[AutoStart..]`
  - **자동재생 토글은 끝이 있는 목록(검색에서 튼 곡 · 앨범 · 플레이리스트)일 때만**, 라디오/믹스(`Radio`)는 토글 없이 알고리즘 칩이 맨 위 (YT Music 과 같음)
  - 칩(전체 · 친숙한 곡 · 2020년대 …)을 고르면 칩 엔드포인트 그대로 `next` → **지금 곡 바로 다음부터** 바꾼다 (`SelectChip` → `LoadAuto(fromNext)`)
  - 앨범 · 플레이리스트 뒤는 automix(`RDAMPL`+목록 ID), 비면 마지막 곡 라디오. 라디오에 곡을 직접 넣으면 `LeaveRadio()` 로 토글 있는 목록이 됨
  - 끌어서 순서 바꾸기 `MoveQueue`, 재생 속도 `Speed`, 이어 듣기 `Snapshot/Restore`(session.json), 취침 타이머 `StopAfterCurrent`
- `LyricsService.cs` — pear synced-lyrics 출처 그대로: LRCLib(기본, Jaro-Winkler · 길이 15초 · 한글/가나 원어 확인) → YT Music → (Musixmatch) → Genius
  - **Musixmatch 는 꺼 둠** (`LyricsService.Disabled`) — 더미(맛보기) 가사만 와서. 다시 켜려면 그 HashSet 에서 빼면 됨
- `Offline.cs` — 다운로드(음악 폴더\OneMusic) · 오프라인 저장(%LocalAppData%\OneMusic\offline)
  - 다운로드 파일에 **TagLibSharp** 로 제목 · 가수 · 앨범 · 앨범아트(가운데 정사각형 JPEG 600px, `SquareJpeg`) 를 넣는다
  - ⚠️ `Instance` 정적 필드는 반드시 `Dir` · `IndexPath` · `DownloadDir` 아래에 (위에 두면 형식 초기화 오류로 ⋮ 메뉴가 통째로 죽었음)
- `Net.cs` — `Net.Handler()`: IPv4 먼저 연결 (IPv6 연결이 몇 초씩 멈추던 문제). StreamResolver · ImageCache · InnerTube 가 쓴다
- `ImageCache.cs` — 이미지 바이트 캐시(LRU 48) + `Prefetch`. 다음 곡 아트 · 마우스 올린 곡을 미리 받아 둔다
- `Settings.cs` — settings.json (볼륨 · 반복 · 자동재생 · 가사 출처 · 알림 · 이어 듣기 · 사이드바 · 창 크기)

## 구조 — UI
- `MainWindow.cs` — pear 배치: 32px 창 끌기 / 90px 상단 바(≡ · 로고 · 검색 480×40 · ‹ › · 기록 · 계정) / 72px 레일 / 화면 / 떠 있는 재생 바.
  앨범 색 번짐 배경(`SetWash`), 스크롤하면 상단 바 유리, ≡ 사이드바(내 재생목록), 상세 화면 쌓기(`OpenBrowse` · `Back` · `Forward`), **미니 플레이어**(`ToggleMini`, CompactOverlay)
- **덮개 층**(Popup 대신 Root 맨 위에 직접 그림 — 그래야 앱 안 블러가 됨): `OneMenu`(⋮ 메뉴) · `Tip`(말풍선) · `Toast` · `SearchDropdown`(검색 추천)
- `BlurBrush.cs` — 진한 유리: backdrop → 가우시안 블러(36~40) → 채도 1.4 → 반투명 색. `Theme.Glass` · `Theme.GlassLight` 가 이걸 쓴다 (AcrylicBrush 안 씀)
- `Views.cs` — 셸프 화면(홈 · 둘러보기 · 보관함: 칩 · 큰 버튼 · 빠른 선곡 · 다시 듣기 2줄 · 격자 · 세로 곡 목록), 검색(맨 위 카드 YT 버튼 · 갈래 '모두 보기')
- `DetailView.cs` — 앨범 · 플레이리스트(두 칸) · 아티스트(머리 + 노래 + 셸프) 상세 화면, 끝까지 내리면 곡을 이어 받는다
- `PlayerView.cs` — 재생 화면: 노래/동영상 전환 + 아트(670×648, 16:9 는 안 자름) / 450px 유리 패널 [다음 트랙 | 가사 | 댓글 | 관련 항목]
  - 다음 트랙 줄은 곡마다 한 번 만들어 다시 쓴다(`_rowCache`), 재생 화면이 닫혀 있으면 열 때 그린다
- `TrackMenu.cs` — ⋮ 메뉴 전부: 좋아요|싫어요 · 다운로드 · YT 메뉴 항목 그대로 · (재생 바면) 현재 재생목록 닫기 · 전문 통계
- `CommentsView.cs` · `LyricsView.cs`(Win2D, 가사 탭이 보일 때만 그림) · `FloatingBar.cs` · `WaveSeekBar.cs`(멈추면 쉼) · `Marquee.cs` · `BarIcons.cs`(모양 바뀌는 아이콘) · `ArtView.cs`(번지듯 바뀌는 아트) · `Rows.cs` · `Controls.cs` · `Extras.cs`(단축키 · 재생 속도 · 취침 타이머 · 알림 · 계정 메뉴) · `MiniPlayer.cs`
- `Anim.cs` — pear style.css 의 linear() 곡선(ease · smooth · spring)을 키프레임으로 (220/440/560ms, 누름 0.9). 만든 애니메이션은 캐시해서 다시 씀.
  윈도우 '애니메이션 효과' 를 끄면 1ms (`Anim.Enabled`, pear prefers-reduced-motion)
- `OneDialog.cs` — 대화상자도 덮개 층(블러 + oneui-pop 스프링). `Prompt.Ask` · YT 확인 대화상자가 쓴다
- `Effects.cs` 의 `Shimmer` — 아트를 불러오는 동안 지나가는 빛 (pear 65), `ArtView.cs` 의 `AmbientGlow` — 재생 화면 아트 뒤 번지는 빛 (pear ambient-mode)
- `ArtView.cs` 의 `ArtBackdrop` — **창 전체 배경 = 앨범아트를 창 크기로 늘려 세게 블러(90) + 채도 1.35**, 위에 어두운 막(Bg 36%) + 옅은 색 번짐. `_wash` 안에 있다
- `Effects.cs`:
  - `Tip` — 말풍선. 크기는 글자(`_text`)만 재서 계산 (상자 DesiredSize 가 재생 화면 열 때 939px 로 튀던 버그). 플로팅 바 안 버튼은 바 위쪽에 뜬다
  - `Skeleton` — 불러오는 동안 회색 자리(셸프 모양 `Shelves` · 상세 화면 모양 `Detail`), 숨 쉬듯 깜빡임. ShelvesView · DetailView 가 '불러오는 중…' 대신 씀
  - `EdgeMask` — **세로 목록 위·아래 끝을 픽셀 단위로 흐림**: 원래 ScrollViewer 의 GPU 투명도 0, VisualSurface 로 비춘 사본에 그라데이션 마스크(형제 host Grid 에 그림). 클릭·휠은 원래 목록이 받음. 댓글 · 다음 트랙 · 관련 항목에 적용
  - `ListFade` — 옛 방식(줄마다 투명도) — 큰 줄이 통째로 흐려지는 문제 있음. 지금은 재생 화면 패널 안 가로 줄(칩 · 관련 항목)에만 남아 있음
- `Anim.ShadowFor(card, z)` — ⚠️ **덮개 층 안의 클릭되는 카드에 ThemeShadow + Translation Z 를 주면 히트 테스트에서 빠진다** (메뉴 항목이 안 눌리던 원인). 그림자는 따로 '그림자만 드리우는' Border 로. OneMenu · OneDialog · SearchDropdown 이 씀
- `NotifyIcons.cs` — 알림 카드 버튼용 흰 아이콘(base64 PNG → AppData 에 풀어 씀). 알림 버튼은 글자 없이 아이콘만(마우스 올리면 설명)
- `TaskbarButtons.cs` — 작업 표시줄 미리보기 이전 · 재생 · 다음 (ITaskbarList3 + WndProc 가로채기, pear taskbar-mediacontrol)

## 성능 메모
- `CompositionTarget.Rendering` 을 구독하면 앱 전체가 매 프레임 다시 그려진다 → 필요할 때만 (가사: 탭이 보일 때, 파도 · 이퀄라이저: 재생 중일 때, 흘리는 글자: 넘칠 때)
- ⚠️ `ScrollViewer.Padding` 은 내용을 잘라 버린다 → 위 여백은 내용 Border 의 Padding 으로 (ScrollPage: `TopInset=122`, 내용이 상단 바 밑으로 흘러 들어가 유리 뒤로 흐리게 비침)
- 목록 끝 흐림은 `EdgeMask`(픽셀 마스크, VisualSurface) — 재생 화면 안 가로 줄만 `ListFade`(화면에 걸친 줄만 계산)
- 붓(`Theme.B`)은 같은 색이면 하나를 같이 씀 → 붓 색을 직접 바꾸지 말 것

## pear 플러그인 대응
- 있음: album-actions(모두 좋아요) · album-color-theme · ambient-mode · blur-nav-bar · custom-output-device · downloader · exponential-volume ·
  navigation · notifications · picture-in-picture(미니 플레이어) · playback-speed · precise-volume · shortcuts · skip-disliked-songs ·
  synced-lyrics · taskbar-mediacontrol · unobtrusive-player · video-toggle
- 일부러 안 함: in-app-menu(사용자 요청) · 외부 서비스용(discord · scrobbler · lumiastream · tuna-obs · music-together · api-server · amuse) ·
  웹 오디오 전용(equalizer · audio-compressor · skip-silences · visualizer · crossfade) · captions-selector · quality-changer · sponsorblock · clock · touchbar(맥)

## 개발용 통로 (PC 에서 작업할 때만, Debug 빌드에서만 켜짐)
- `dev-watch.bat` 을 켜 두면 `.dev\cmd.txt` 에 `build` / `run` / `stop` / `mem` / `publish` 를 적을 때 실행하고 `.dev\out.txt` 에 결과를 남긴다
- 앱(Debug)은 `.dev\app-cmd.txt` 의 명령을 받는다: `shot` · `state` · `mem` · `nav:home|player|lyrics|close` · `search:검색어` · `open:browseId` · `back` · `play:first|곡ID` · `next` · `toggle` · `seek:초` · `login` · `auth` · `ytdlp:곡ID|클라이언트` · `dump:곡ID`
  - 진단용(새로 추가): `menutest`(메뉴 히트 테스트) · `suggest:검색어`(추천 드롭다운 상태) · `tiptest`(말풍선 위치) · `download`(지금 곡 다운로드 + 태그 확인) · `comments`(댓글 파싱 결과) · `notify`(알림 띄우기) · `ptab:0~3`(재생 화면 탭 열기) · `hit:x,y`(그 자리 요소 나열, DIP 좌표)
  - 마지막 API 응답은 `.dev/last-*.json` 에 저장됨 (파싱 디버깅용)
- 앱 로그: `.dev\app.log` (배포판은 `%LocalAppData%\OneMusic\log.txt`)

## 사용자 취향 (중요)
- 한국어 반말로 친구처럼 대화
- 디자인 기준은 **삼성 One UI** + 사용자가 만든 버스 앱(Compose) · pear-desktop One UI 테마. 다크 모드만
- **블러 듬뿍**(유리 표면), 앨범 색 배경, 중요한 곳 Bold, iOS 스타일 스프링 애니메이션, 네이티브 느낌(스크롤바 없음 · 글자 선택 없음)
- 파도 재생 바: 직선은 그대로, 재생한 쪽 위로 뾰족한 /\ 언덕이 제각각 진하기 · 속도로 흐르고, 멈추면 그 자리에서 부드럽게 가라앉음
- 아이콘: 꺼짐/음소거는 회색, 켜짐은 흰색. 볼륨 슬라이더는 스피커 아이콘에 올릴 때만 펼쳐짐
- 가로 목록은 마우스 휠로 안 넘어간다 (재생 화면 알고리즘 칩 줄만 예외). 메인 화면 16:9 아트는 가운데 기준 1:1 로 자름
- pear 의 앱 메뉴 막대(확장 · 설정 · 보기 …)는 만들지 않는다 — 기능만
- 끝 흐림은 **요소마다 투명도 X → 위·아래 정해진 띠에서 픽셀 그라데이션**
- 로딩은 글자보다 회색 자리(스켈레톤) → 진짜 내용으로 전환
- 배경은 단색 그라데이션보다 **앨범아트 자체를 크게 블러**해서 앨범 색이 다 보이게
- 레일과 내용 사이는 살짝만, 플로팅 바 요소들은 너무 붙지 않게
- 작업 방식: 고치고 → PC 에서 빌드 → 앱 띄워서 스크린샷으로 직접 확인 → 결과 보고. 깃허브 push 금지

## 단축키 (Shift + ? 로 앱에서도 볼 수 있음)
Space/K 재생 · Shift+N/P 다음/이전 · J/L 10초 · ←/→ 5초 · ↑/↓ 볼륨 · M 음소거 · R 반복 · S 셔플 · +/− 좋아요/싫어요 ·
F 재생 화면 · I 미니 플레이어 · Shift+</> 재생 속도 · / 또는 Ctrl+F 검색 · Alt+←/→ 뒤로/앞으로 · Esc 닫기/뒤로

## 최근 작업 (2026-10-21) — 완료
- ⋮ 메뉴 안 열림/안 눌림(Offline 초기화 순서 · ShadowFor), 가사 아래 잘림, 재생 화면 아트 빛 가장자리, 곡 · 아트 로딩 속도(IPv4 · yt-dlp 폴더판 · 미리 받기 · 이미지 캐시)
- 창이 처음에 쪼그맣게 뜸(최소화 상태에서 크기 저장하던 버그 → `PlaceWindow` 가 검증), 창 좁을 때 플로팅 바 잘림(`FloatingBar.Fit`)
- 알림 카드에 이전/재생/다음 버튼(아이콘만), 다운로드 앨범아트, 상단 바 밑으로 내용 블러, 추천 검색어(포커스가 검색창 안쪽 입력칸에 있어서 `SearchHasFocus()` 로 확인)
- 트랙 출처 ↔ 칩 간격, 플로팅 바 아래 줄 6px 내림, 앨범아트 블러 배경, 말풍선 위치, 미니플레이어 아이콘 회색(셔플 · 반복처럼 55%)
- 댓글 안 보임(이제 `commentViewModel` 안에 `commentViewModel` 이 한 겹 더 감싸져 옴 → `FromView` 에서 벗김), '댓글 댓글 24개' 중복
- 레일 ↔ 내용 여백 줄임(`ScrollPage.Left = 16`), 스켈레톤 로딩, Musixmatch 끔, 세로 목록 픽셀 흐림(`EdgeMask`)

## 최근 작업 (2026-10-21 오후) — 완료
- 검색창 밑 'mc몽' 알약: 원인은 앱이 뜰 때 윈도우가 검색창에 저절로 주는 포커스(Programmatic) → GotFocus → 최근 검색어(settings.json `RecentSearches`) 드롭다운이
  유리 배경이 그려지기 전에 떠서 글자 줄만 남아 있던 것. 이제 Programmatic 포커스엔 안 띄움 (`/` · Ctrl+F 는 `_focusWanted` 로 띄움)
- 메인 화면 스크롤 그라데이션: `EdgeMask` 에 가로(`horizontal`) · StackPanel 부모(Grid 로 한 겹 감쌈) 지원.
  셸프 가로 줄 · 칩 줄은 `HorizontalRow(pixelFade: true)` = EdgeMask, 재생 화면 안 가로 줄은 `pixelFade: false` = 옛 ListFade (이미 EdgeMask 목록 안이라)
  ScrollPage 는 아래만 EdgeMask(재생 바 높이 + 여백 + 40), 위는 상단 바 유리가 대신. `WashFade` 는 지움
- 기본 창 크기: 작업 영역 57% (예전 85% 의 2/3, 최소 1000×680). Settings 기본값 0 → 처음엔 화면 비율로

## 최근 작업 (2026-10-21 저녁) — 완료
- 기본 창 크기 1199×820 고정(`DefaultWidth/Height`, 화면보다 크면 맞춤). 저장된 크기가 있으면 그게 우선
- 플로팅 바 클릭이 뒤로 통과: 바에 직접 준 ThemeShadow + Translation Z 때문 → `Anim.ShadowFor(Bar, …)` 받침으로 (FrameworkElement 판 추가)
- 아이콘: `src/Assets/app.ico`(앱아이콘.svg → 16~256, `ApplicationIcon` + `AppWindow.SetIcon`) · `src/Assets/logo.png`(내부 아이콘.svg, 'Music' 글자 path 6개 fill #000 → #fff, 3배 해상도).
  SVG 가 CSS 클래스 · clip-path 투성이라 SvgImageSource 대신 Edge headless 로 PNG 로 구움. 로고 누르면 `GoHome()`
- 스크롤 흐림이 끝에서 뚝 끊김 → `EdgeMask` 끝 색을 300ms 애니메이션, `ListFade` 는 남은 거리만큼 띠가 줄어듦
- 16:9 아트 왼쪽 기준으로 잘림 + 둥근 모서리 계단: ⚠️ 둥근 Border 안의 Image '자식'은 (1) 자르기가 AA 없음 (2) UniformToFill 로 넘치면 왼쪽부터 놓임.
  → `Cover`(src/UI/Cover.cs): 그림을 둥근 Border 의 ImageBrush 배경으로(가운데 정렬, AA), 확대는 붓 RelativeTransform.
  컴포지션 아트(`ArtView`)는 기하 자르기 대신 `RoundMask`(Win2D 로 그린 AA 둥근 사각형 9-grid 마스크). 새 이미지는 이 둘로 만들 것
- ≡ 사이드바 재생목록: 마우스 올리면 오른쪽 흰 원 재생 버튼 → `Play.Card(c with { PageType = "play" })`
- 메인 배경: `BuildHomeBackground()` — YT Music 기본(위 520px 가로 그라데이션 청록 #15333a → 회청 → 올리브 #464a40, 아래로 #17171a 에 녹음).
  앨범아트 블러 · 색 번짐(`_wash`)은 재생 화면 열 때만 나타남
- 플로팅 바 줄: 요소 중심은 다 맞았는데 줄이 바 바닥에 붙어(위 17 · 아래 8) → 파도 선과 바닥 사이 가운데로 4px 올림
- 검색 드롭다운이 켜자마자 뜸(창이 뜰 때 포커스가 Pointer/Keyboard 로도 옴) → 검색창 위에서 실제로 누름 · Tab · / · Ctrl+F 일 때만
- 개발용 명령 추가: `cards`(카드 아트 크기 · 주소), `guide`(사이드바 토글)

## 최근 작업 (2026-10-21 밤) — 완료
- 플로팅 바 아무 데나 누르면 재생 화면 (`FloatingBar.Tapped`). 가수 이름만 아티스트 화면, 버튼 · 파도 바 · 볼륨 슬라이더는 Tapped 를 Handled 로 막음
- 바 아이콘 보이는 크기 통일 (24 칸 안 그림 크기가 달라서): 볼륨 27 · 반복/셔플 22 · PIP 25 → 보이는 높이 약 16
- 메인 배경: 사용자 YT Music 화면에서 카드 · 글자를 빼고 바탕색만 24×16 격자로 뽑아 부드럽게 늘린 그림 `Assets/home-bg.png`
  (왼쪽 위 청록 · 가운데 왼쪽 올리브 · 오른쪽 위 블루 · 가운데 오른쪽 진한 남색 → 아래 #17171a). UniformToFill · 위 정렬
- 둘러보기: 맨 위 큰 버튼은 '제목 없는 격자'의 버튼만(최신 음악 · 차트 · 분위기 및 장르). 셸프 안 버튼은 `Shelf.Buttons` 로 따로 →
  '분위기 및 장르' 셸프는 4행 가로 격자 + ‹ › · 더보기, 분위기 및 장르 화면은 구역별 4칸 격자
- 보관함: 위에 One UI 알약 탭(`PillTabs`) [보관함 | 오프라인 저장]. 오프라인 저장 탭은 이 PC 에 저장한 곡 목록 (전엔 보관함 맨 위 셸프)
- 여백: `ScrollPage.Left = Right = 48` — 홈 · 둘러보기 · 보관함 · 검색 · 상세 화면 공통
- 상단 바 로고 계단 → 3배 그림을 GPU 가 줄이던 것. `DecodePixelHeight = 26, Logical` 로 디코더가 화면 크기로 줄임
- 켤 때 바로 재생: `Settings.PlayOnStart`(계정 메뉴 '켤 때 바로 재생'), `Restore(session, play)`. 곡이 바뀔 때마다 session 저장(강제 종료해도 이어짐)
- 음악 디스크 캐시: `Core/AudioCache.cs` — %LocalAppData%\OneMusic\cache\audio\곡ID.m4a, 다 받은 곡을 저장, 다시 틀면 yt-dlp 없이 바로.
  상한 `Settings.CacheLimitMB`(기본 2GB, 계정 메뉴 '음악 캐시' → 끄기/512MB/1/2/5/10GB · 비우기). 넘으면 오래 안 들은 곡부터 지움
- ≡ 사이드바 홈 · 둘러보기 · 보관함 아이콘도 고른 탭만 채움 (레일과 같게)
- 개발용 명령: `nav:explore` · `nav:library` · `scroll:y` · `libtab:0|1`

## 최근 작업 (2026-10-21 밤 2) — 완료
- 새 재생목록 대화상자 `UI/NewPlaylistDialog.cs` (사이드바 · 곡 ⋮ · 다음 트랙 저장 공통): 제목 · 설명(여러 줄) · 공개 범위(`PillTabs` 공개/일부 공개/비공개, 기본 비공개) · 공동작업(`OneToggle`).
  `CreatePlaylist(title, ids, privacy, description)`, 공동작업은 만든 뒤 `EnableCollaboration` = browse/edit_playlist `ACTION_CREATE_COLLABORATION_INVITE_LINK` (ytmusicapi 와 같음) → 초대 링크 클립보드
- 메인 가로 줄: 스크롤 칸 자체를 좌우 여백(Left/Right) 안쪽으로 → 넘겨도 여백으로 안 나감. ‹ › 는 카드 왼쪽 끝에 맞춰 멈춤(`SnapTarget`). 끝 흐림 40
- 작업 표시줄 미디어 창 '알 수 없는 앱' → `Core/AppIdentity.cs`: 프로세스 AUMID `Cheongsagwa.OneMusic.YouTubeMusic` + 시작 메뉴 바로가기
  `%AppData%\Microsoft\Windows\Start Menu\Programs\YouTube Music.lnk`(같은 AUMID · 아이콘) + 레지스트리 AppUserModelId(DisplayName · IconUri). 알림도 `Register(이름, 아이콘)`
- 앱 이름 YouTube Music (창 제목 · AssemblyTitle · Product). 실행 파일 · 데이터 폴더(%LocalAppData%\OneMusic) 이름은 그대로
- 재생 바 아트가 켜자마자 빈 칸: 첫 아트는 '가운데서 번지는 마스크' 애니메이션으로만 보이는데 앱 시작 직후엔 애니메이션이 안 돌아 반지름 0 에 멈춤 → 760ms 뒤 최종 상태를 직접 지정
- 개발용 명령: `newpl`(대화상자만 띄움) · `dialog:cancel` · `shelfnext:n`

## 최근 작업 (2026-10-21 밤 3) — 완료
- ≡ 사이드바 펼친 채 재생 화면을 열면 뒤로 비침 → OpenPlayer/ClosePlayer 가 `_guide` 도 숨김/복원, 사이드바가 열려 있으면 레일은 계속 숨김
- 곡 줄 마우스 오버 때 아트 모서리 깨짐: 둥근 아트 위에 둥근 검은 덮개를 따로 얹으면 두 도형 가장자리 픽셀이 겹쳐 모서리에 옅은 테두리가 생김 →
  `Cover.Dim()` 으로 아트 자체 투명도를 낮추고 재생 아이콘만 얹음 (곡 줄 · 검색 결과 줄). 아트가 뜨면 밑의 회색 칸(`_placeholder`)도 치움(같은 이유)
- 댓글 속 시간(분:초 · 시:분:초)은 흰 바탕 검은 글씨 알약(`CommentsView.TimePill`, RichTextBlock 의 InlineUIContainer) — 누르면 그 위치로 넘기고 멈춰 있었으면 재생. 곡 길이를 넘으면 토스트
- 가사 출처 카드(`LyricsSourceBar`)는 3초만 보이고 접힘(높이 84 → 0). 가사 탭 열 때 · 곡/출처 바뀔 때 · 가사 위쪽 90px 에 마우스 올릴 때 다시, 카드 위에 있는 동안은 안 접힘

## 최근 작업 (2026-10-21 밤 4) — 완료
- 댓글 시간 알약: 높이 20 · 반지름 10 캡슐 (반지름 8 은 낮은 해상도에서 각져 보임). 댓글 본문 줄 간격 21 고정(BlockLineHeight)
  (EdgeMask 를 끄고 비교해 봤지만 픽셀 차이 52개 — EdgeMask 는 원인 아님)
- 5줄 넘는 댓글은 접힘 + '자세히 보기' 알약 → 높이 애니메이션으로 펼침, '간략히' 로 접기 (`CommentsView.Collapsible`, 개발용 `expandfirst`)
- 볼륨이 껐다 켜면 예전 값: 설정은 창 닫을 때만 저장했음 → 볼륨 · 음소거 바뀌면 0.5초 뒤 저장(`Settings.SaveSoon`)
- 좋아요 · 싫어요 아이콘: YouTube 현재(Cairo) 아이콘 — fonts.gstatic.com/s/i/short-term/release/youtube_outline|youtube_fill/svg/thumb_up_cairo_24px.svg,
  싫어요는 180° 회전. `Icons.ThumbUp/ThumbDown` 은 "@thumb_up" 표시 → `Theme.Icon`/`IconFilledStroke` 가 채움 도형으로 그림
- 플로팅 바 오른쪽 아이콘(볼륨 · 반복 · 셔플 · 미니 플레이어)은 새로 그린 한 벌 (BarIcons.cs, `BarIcon.StrokeW = 2`):
  24px 배율 1, 굵기 정수 2, 곧은 변은 정수 좌표 → 두 픽셀에 딱 맞아 번지지 않음 (1.7 굵기는 어디 둬도 반 픽셀씩 걸쳐 흐렸음).
  모두 가로 18 · 세로 14~18 · 세로 중심 같음. 반복은 화살촉 두 개(위 →, 아래 ←)로 위아래 대칭, 볼륨은 외곽선 스피커 + 동심원 물결, PIP 는 `PipIcon`.
  ⚠️ YouTube 플레이어 아이콘(1px 선)으로 바꿔 봤지만 사용자가 싫어해서 되돌림
- 앨범아트(`ArtView`): 화면 픽셀 크기로 디코딩(가로는 16/9 여유) — 2배로 읽어 GPU 가 4:1 로 줄이며 깨졌음

## 최근 작업 (2026-10-21 밤 5) — 완료
- 볼륨 아이콘: 물결 · X 를 점선 길이로 지웠다 그렸다 하는데, 다 지운 획도 둥근 끝이 점으로 남음(숨은 X 끝이 물결 옆에 삐죽, 음소거 X 위에 물결 점)
  → 다 지운 획은 Opacity 0. 스피커 나팔 끝 예각 꼭짓점은 둥글게(가시처럼 튀어나왔음)
- 재생 화면 16:9 아트가 가운데 정사각형으로 잘림: 둥근 모서리 마스크를 '작은 정사각형 9-grid' 로 만들어서 마스크 붓이 그 정사각형 비율로 내용을 잡았음.
  → `RoundMask.Create(c, radius, size, scale)` 가 칸 크기 그대로 둥근 사각형을 그림, `ArtView.Resize` 가 칸 크기 바뀌면 마스크 다시.
  VisualSurface 로 층 전체를 비추던 방식은 뺐음(그림 붓마다 `Round()` 마스크). 마스크 붓은 겹칠 수 없어서(ArgumentException)
  새 아트 등장은 '가운데서 번지는 원형 마스크' 대신 서서히 나타나며 1.04 → 1 로 자리 잡기
- 개발용: `play:곡ID|썸네일주소`, `mute`

## 최근 작업 (2026-10-22) — 완료
- 설치 프로그램(`installer/`, WPF 한 파일, `build-installer.ps1` → `dist/YouTubeMusicSetup.exe`): 시작 메뉴 · 바탕화면 · 설치 후 실행 선택, 업데이트 모드, uninstall.cmd + 제어판 등록
- 앨범아트가 가끔 안 바뀜: `LoadedImageSurface` 를 아무도 안 붙잡아서 GC 가 거둬 가면 LoadCompleted 가 안 옴 → `ArtLoader` 가 끝날 때까지 붙잡아 둠. 개발용 `artgc:N` 으로 확인 (앱 재시작 후 아직 실행 확인 안 함)
- 다운로드 폴더: 음악\OneMusic → 음악\YouTube Music (예전 폴더 자동으로 옮김)
- 뮤직비디오 전환이 소리로만 나옴: 영상 주소(18) 403 → DASH 목록으로 바꿈 (앱 안에서 1080p 재생 확인)
- 재생 화면 앨범아트가 창을 키워도 그대로: Viewbox 대신 `FitArt` 로 크기 계산 + `ArtView` 가 커지면 더 큰 그림을 다시 받음
- 다음 트랙 줄: Windows 끌어 놓기 → 포인터 잡고 직접 움직이기 (위아래 = 순서, 옆으로 1/3 넘게 = 목록에서 빼기, 지금 곡은 안 빠짐). 마우스로 실제 손맛 확인은 아직
- 뮤직비디오 위에 마우스 → 위쪽 그라데이션 + 전체 화면 버튼 (더블클릭 · Esc 도). 영상(MediaPlayerElement)을 창 전체 판으로 옮겨 붙임.
  DevBridge `fs` 로 켜기/끄기 · 창 위치 복원 확인. 전체 화면에서 영상이 실제로 보이는지는 눈으로 확인 필요 (`shot` 은 영상 스왑체인을 못 찍어 검게 나옴, 이 앱은 computer-use 정책상 차단)
- 세로 창(높이 > 너비)이면 재생 화면이 2행: 위 아트 · 아래 패널 (`PlayerView.Arrange`). 화면 확인은 아직
- 알고리즘 칩이 앱을 다시 켜면 사라짐: 이어 듣기(`SessionState`)에 `AutoChips` 를 안 남겼음 → 남기고, 칩 없는 예전 세션이면 `RefetchChips` 로 칩만 다시 받음
- 칩 줄이 있을 때 처음 열면 맨 위인데 위쪽 흐림: `ScrollToCurrent` 가 2px 쯤 내려서 → 24px 안이면 0 으로
- 애니메이션: `Anim.Ease/SmoothCurve/Spring` 을 iOS 스프링(감쇠 진동자 샘플, `Anim.SpringAt`)으로, `Tween.Out` 도 스프링.
  `OneToggle` 노브 이동 · 색 크로스페이드 · 누르면 늘어남, `Anim.FadeBg`(호버 배경 페이드)를 모든 호버 배경에,
  플레이어 아트 크기 바뀜 `MorphArt`, 자동재생 켜면 섹션이 떠오름
- 동영상 연결: 곡 ⋮ → '동영상 연결' (`VideoLinkDialog`, yt-dlp `ytsearch10` = `StreamResolver.SearchVideos`, 길이 ±3초가 위로).
  `VideoLinks` (video-links.json, 음원 id → 영상 id) 를 `PlayerService.VideoIdFor` 가 짝(CounterpartId)보다 먼저 본다.
  예: Main Theme(Rfp9SZe2nHw, 무단 음원) ↔ 원본 영상 Ck53uo-KSmU — YT 가 짝을 모른다.
  자동 연결: 짝 없는 음원을 열면 `PlayerService.AutoLink` → `StreamResolver.FindOriginalVideo` ('앨범 제목' → '제목 가수' 검색,
  제목 포함 · 길이 ±3초 · '- Topic' 아님). 없음은 "" 로 기억(다시 안 찾음), 연결 해제도 "" 로. DevBridge `findvideo:id|제목|가수|앨범|초`
- 재생목록 · 앨범 화면 배경: 그 목록 아트 블러 (`MainWindow._pageWash` + `_pageBackdrop`, 아래로 갈수록 어두운 그라데이션).
  `DetailView.Art/ArtReady` → `SyncPageBg` (열기 · 뒤로 · ClearDetails 때). 아티스트 화면은 기본 배경
- 재생목록 스켈레톤(`Skeleton.Detail(left,right)`)을 `BuildTwoColumn` 치수와 똑같이. 버튼 7개(좋아요 표시한 음악)면 360 칸을 넘어 잘려서 간격을 자동으로 줄임
- 음량 표준화 (계정 메뉴 토글, `Settings.Normalize` 기본 켜짐): ANDROID_VR player 응답의 `playerConfig.audioConfig.loudnessDb`
  (`StreamResolver.Loudness`, 캐시 · 다음 곡 미리) → `GainDb = clamp(-loudness, ±12)`, `_mp.Volume = min(1, 곡선(볼륨) × 배율)`.
  볼륨 배율만 바꾸니 음질 손실 0, 대신 키우는 쪽은 볼륨 100% 가 상한. 1.5초 안에 못 받으면 0dB 로 시작하고 오면 맞춤. DevBridge `state` 에 `norm/gain`
- DevBridge: 읽은 명령은 지움 (Set-Content 가 파일을 두 번 건드려 같은 명령이 두 번 돌던 문제), `state` 에 fullscreen 줄
- 재생 안 되는 곡(예: Inside Tracks · Challenger 앨범 — KR 지역 제한): yt-dlp 가 'Video unavailable' 이면 `UnplayableException`
  → yt-dlp 업데이트 · 재시도 없이 토스트 + 다음 곡 (연속 5곡까지)
- 짝(CounterpartId)이 없는 곡이 많음(목록 50곡 중 14곡만) — 동영상 버튼이 꺼지는 곡이 많은 이유. 필요하면 따로 짝 찾기

## 최근 작업 (2026-10-23) — 완료
- 이어 준 영상으로 볼 땐 영상 제목 · 채널 · 썸네일로 보임: `PlayerService.ShowAs` → `_shownAs` 를 `Current` 가 돌려줌 (바 · 플레이어 · 다음 트랙 줄 · 작업 표시줄 SMTC 모두).
  영상 정보는 `VideoMeta` (video-meta.json) / 없으면 `StreamResolver.VideoInfo` (yt-dlp). SMTC 는 `_item` 에 `SetDisplay` 다시 — 반드시 원래 곡으로 깐 뒤에 ShowAs (거꾸로면 원래 곡이 남음)
- 자동 연결: 못 찾음은 저장 안 함(다음에 다시 찾음). 앨범 → 채널 힌트(video-album-channels.json): 'OWCS KOREA 2026 Track.04' 는 안 나오고 '호람썬 Track.04' 는 나와서
- 플로팅 바 아트 둥근 모서리 흰 줄: `ArtView` 빈 배경(흰 20)이 아트 가장자리에 비침 → 아트가 뜨고 760ms 뒤 배경 없앰(`DropBackdrop`)
- 다음 트랙 머리(출처 + 저장): 자동재생 줄이 없으면 목록과 간격 20 (탭 ↔ 저장 간격과 같게)

## 다음에 할 일 (미완성)
1. 확인 못 한 것: 재생 화면에서 스크롤하다 재생 화면이 혼자 닫힌 적 있음(원인 모름), 재생 화면 EdgeMask 위쪽 흐림 화면 확인, 창 좁을 때 바 `Fit`, 마우스 올려 미리 받기 체감
2. 개발용 진단 코드(DevBridge 명령들)는 Debug 전용이라 둬도 됨
- 동영상 화면: 재생 화면 내릴 때 잔상(스왑체인은 부모 애니메이션을 안 따름) → Close 에서 바로 숨기고 Open 380ms 뒤 다시. 카드 안에선 UniformToFill(검은 테두리 없앰), 전체 화면만 Uniform
- 동영상 전환이 가사 · 팬 영상으로 됨: 재생목록 · 앨범에서 받은 곡은 짝(CounterpartId) 없이 와서 검색으로 빠졌음 → AutoLink 가 먼저 `InnerTube.Counterpart` (그 곡만 next) 로 YT 공식 짝을 물어 `_pairs` 에 (예: ETA XCkpalH6Ppk → jOTfBlKSQYY). 검색은 YT 도 모르는 곡만, 가사/커버/라이브/리믹스 등 거르고 가수 채널 먼저. DevBridge `pair:id`, `state` video 줄에 id
- 높은 음질(계정 메뉴 토글, `Settings.HighQuality` 기본 켜짐): 로그인 + node/deno 있으면 `StreamResolver.ViaPremium` = 쿠키를 잠깐 Netscape 파일로(`Auth.WriteNetscape`, 바로 지움) → yt-dlp `web_music` + `--js-runtimes` 로 itag 141 (AAC 256k). 주소 받는 데 ~4초(미리 받기로 가림). 141 이 3번 연속 안 오면 이번 실행 동안 끔. 디스크 캐시는 256k 를 `.hq.m4a` 로 따로 (예전 128k 캐시는 높은 음질일 때 안 씀). 전문 통계 맨 위 '음질' 줄. 동영상 모드 소리 · 오프라인 저장본은 아직 128k. DevBridge `stats`
- 검색에 위 카드 하나만 나옴: YT 검색 응답이 바뀌어 갈래(musicShelfRenderer) 없이 itemSectionRenderer 마다 한 줄씩 옴 → `InnerTube.Search` 가 그것도 모아 '검색 결과' 한 덩어리로 (예전 형식도 그대로 읽음)

- 메모리 최적화 (블러·애니메이션 품질은 그대로): ① Cover 그림은 화면 1500px 안에 들어올 때 받음 (EffectiveViewportChanged, 한 번 받으면 끝) ② 상세 화면 스택은 바로 밑 1장만 살아 있고 더 아래는 빈 DetailView 껍데기 → Back 때 _content 에 다시 넣고 Load() ③ 메모리 오디오 6곡→3곡 (나머진 디스크 캐시) ④ Core/MemoryTrim: 최소화하거나 1분 넘게 비활성이면 GC 압축 + SetProcessWorkingSetSize(-1,-1) (30초 간격 제한). DevBridge mem/memgc 로 측정 (ws·private·gc 힙). 측정: 곡 5개 넘긴 뒤 private 464→419MB, 페이지 왕복 3번 519→493MB, 백그라운드 1분 뒤 작업 집합 ~470→~210MB. 나머지 ~250MB 는 WinUI·D3D·MediaPlayer 기본값 (.NET 힙은 45MB 정도)

- 설치파일: 이미 깔려 있으면 (레지스트리 InstallLocation → 기본 폴더 순으로 install.json 확인) MaintainPage 로 시작 — 업데이트(지난 바로가기 선택 그대로, 끝나면 실행) / 제거(앱 끄기 · 바로가기 · 레지스트리 · 폴더 삭제, %LocalAppData%\OneMusic 계정·설정은 남김). 없으면 원래 옵션 화면

- 설치파일 UI 정리 (지침: 보조 설명 없애기): 부제 · 토글 설명 · 버전 줄 · 진행 중 파일 이름 · 퍼센트 · 끝 화면 설명(실패 때만 보임) 없앰. '설치가 끝나면 실행' 토글과 자동 실행 없앰 — 끝 화면 '열기'와 같은 일. 크기 글자 대신 payload 가 없을 때만 빨간 ErrorText. IProgress 는 값(double)만
- ⚠️ 함정: PowerShell 에서 함수 이름을 R 로 지으면 기본 별칭 R(Invoke-History)이 먼저 잡혀 함수가 안 불린다 (파일이 그대로 다시 써짐)

## 앱 UI 지침 정리 (2026-10-23)
- 메뉴 켜기/끄기는 `OneMenu.Toggle(text, icon, get, flip)` — 오른쪽 OneToggle 스위치(줄이 누르기를 받음, 스위치는 IsHitTestVisible=false), 메뉴는 열린 채. 토스트 안 띄움.
- 값이 있는 줄은 `Item(..., value: ...)` 로 오른쪽 정렬 흐린 글자, 고른 항목은 `check: true` 로 오른쪽 체크 — 이름 뒤에 ` · 값` / `  ✓` 를 붙이지 않는다. 헤더 값도 `Header(text, value)`.
- 여러 줄 TextBlock(Theme.Text maxLines>1)은 `Theme.KeepAll` 로 한글 음절 사이에 U+2060 — WrapWholeWords 여도 '트/랙' 처럼 음절 사이에서 잘리던 문제.
- 재생 바 시간 말풍선(Tip.ShowNow)은 창 PointerExited · 창 비활성 · PointerCaptureLost 에서도 닫는다 — 요소 PointerExited 만으론 창 밖으로 빠르게 나가면 남았다.
- 토스트는 이상 상태 · 해야 할 행동 · 화면에 안 보이는 결과일 때만 (실패, 높은 음질 조건 미충족, 취침 타이머 작동, 캐시 비움, 재생목록 생성 등). 아이콘/글자로 이미 보이는 선택(속도 · 출력 장치 · 반복 · 취침 설정)은 토스트 금지.
- 칸 위에 이름 라벨이 있으면 placeholder 는 비운다. 설정 줄에 설명 줄 달지 않는다.
- 메뉴 순서는 자주 쓰는 순. 곡 · 카드 메뉴는 `TrackMenu.Rows` 에 (순위, 줄) 로 모아 `RankOf(endpoint, iconType)` 순서로 늘어놓고 십의 자리가 바뀌면 구분선 — 1x 재생 · 대기열, 2x 이동, 3x 보관 · 공유, 4x 드문 것. YT 가 주는 순서에 기대지 않는다. 곡 메뉴에서 YT 의 '좋아요 표시한 노래에 추가'는 맨 위 좋아요 줄과 겹쳐서 뺀다. 계정 메뉴: (로그인 전이면 로그인) → 속도 · 출력 · 취침 → 소리 설정 → 앱 동작 → 폴더 · 캐시 · 단축키 → 로그아웃.
- DevBridge `trackmenu` = 재생 바 ⋮ 메뉴 열기.

## 리디자인 (OneMusic-Redesign)
- 버전은 저장소 맨 위 `Directory.Build.props` 의 `<Version>` 한 곳 — 앱(앱 정보 창)과 설치 프로그램(설치할 버전 · 레지스트리 DisplayVersion · install.json)이 같이 쓴다. 각 csproj 에 `<Version>` 다시 넣지 말 것. 올리는 규칙: 큰 변화 major · 기능 minor · 버그 patch. 리디자인은 2.0.0 부터.
- 설치 프로그램은 깔린 버전을 install.json 의 version 에서 읽어 "현재 → 새" 로 보여 준다 (모르면 새 버전만).
- 내 계정 이름은 `ShelvesView.Me` — 부제 · 머리글에서 `WithoutMe` 로 뺀다 (내 보관함에 내 이름 반복 금지).
- 기록은 보관함 탭(1)으로 옮겼고 위쪽 뒤로/앞으로 버튼은 없앴다 — 마우스 X1/X2 · 단축키로만.
- 상세 화면 ⋮ 는 `TrackMenu.ShowCard(at, card, pos, pageRows)` — page 를 넘기면 재생 · 열기 줄은 빠진다.
- 버튼 하나짜리 대화상자는 `OneDialog.Show(..., close: "")`.
