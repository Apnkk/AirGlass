import React from 'react';
import {AbsoluteFill, Audio, Easing, Loop, OffthreadVideo, Sequence, interpolate, staticFile, useCurrentFrame} from 'remotion';

// 30 fps: intro 2 s, iPhone story 14 s, outro 3 s = 19 s.
// Audio cues in scripts/make-audio.mjs are timed on these frames (Story starts at frame 60).
const INTRO = 60;
const STORY = 420;
const OUTRO = 90;
export const TOTAL_FRAMES = INTRO + STORY + OUTRO;

// Palette taken from App.xaml.
const BG = '#0B0C0F';
const TEXT = '#ECEDEF';
const MUTED = '#8B909C';
const ACCENT = '#3D8BFF';
const BORDER = 'rgba(255,255,255,0.12)';
const FONT = '"Segoe UI Variable Display", "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif';

const clamp = {extrapolateLeft: 'clamp', extrapolateRight: 'clamp'};
const easeOut = Easing.bezier(0.16, 1, 0.3, 1);
const easeInOut = Easing.bezier(0.65, 0, 0.35, 1);

const progress = (frame, from, to, easing = easeOut) =>
  interpolate(frame, [from, to], [0, 1], {...clamp, easing});
const lerp = (a, b, p) => a + (b - a) * p;

/** Faint grid drifting slowly behind everything. */
const GRID_MASK = 'radial-gradient(ellipse at 50% 50%, #000 0%, transparent 75%)';
const Backdrop = () => {
  const f = useCurrentFrame();
  const shift = (f * 0.4) % 80;
  return (
    <AbsoluteFill
      style={{
        backgroundImage:
          'linear-gradient(rgba(255,255,255,0.045) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.045) 1px, transparent 1px)',
        backgroundSize: '80px 80px',
        backgroundPosition: `${-shift}px ${-shift}px`,
        WebkitMaskImage: GRID_MASK,
        maskImage: GRID_MASK,
      }}
    />
  );
};

/** Fades its children in at the start and out at the end of the current sequence. */
const Fade = ({duration, fadeIn = 10, fadeOut = 12, children}) => {
  const frame = useCurrentFrame();
  const opacity = Math.min(
    interpolate(frame, [0, fadeIn], [0, 1], clamp),
    interpolate(frame, [duration - fadeOut, duration], [1, 0], clamp)
  );
  return <AbsoluteFill style={{opacity}}>{children}</AbsoluteFill>;
};

/** AirGlass logo (same geometry as the LogoImage resource in App.xaml). */
const Logo = ({id, size, draw = 1, pop = 1}) => (
  <svg width={size} height={size} viewBox="0 0 64 64">
    <defs>
      <linearGradient id={id} x1="0" y1="0" x2="1" y2="1">
        <stop offset="0" stopColor="#1E2333" />
        <stop offset="1" stopColor="#0B0D12" />
      </linearGradient>
    </defs>
    <rect x="0.75" y="0.75" width="62.5" height="62.5" rx="15" fill={`url(#${id})`} stroke="#2E3547" strokeWidth="1.5" />
    <path
      d="M25,43 L15,43 L15,19 L49,19 L49,43 L39,43"
      fill="none"
      stroke="#F2F4F8"
      strokeWidth="3.5"
      strokeLinecap="round"
      strokeLinejoin="round"
      pathLength="1"
      strokeDasharray="1"
      strokeDashoffset={1 - draw}
    />
    <path
      d="M32,35 L43,49 L21,49 Z"
      fill={ACCENT}
      stroke={ACCENT}
      strokeWidth="2"
      strokeLinejoin="round"
      opacity={pop}
      transform={`translate(32 43) scale(${0.4 + 0.6 * pop}) translate(-32 -43)`}
    />
  </svg>
);

const Intro = () => {
  const frame = useCurrentFrame();
  const draw = progress(frame, 0, 22, easeInOut);
  const pop = progress(frame, 18, 32);
  const word = progress(frame, 14, 34);

  return (
    <Fade duration={INTRO} fadeIn={1}>
      <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center'}}>
        <div style={{display: 'flex', alignItems: 'center', gap: 48}}>
          <Logo id="logo-intro" size={220} draw={draw} pop={pop} />
          <div style={{overflow: 'hidden', padding: '0 8px 16px 0'}}>
            <div
              style={{
                fontSize: 168,
                fontWeight: 600,
                lineHeight: 1.1,
                letterSpacing: '-0.03em',
                transform: `translateY(${(1 - word) * 110}%)`,
              }}
            >
              AirGlass
            </div>
          </div>
        </div>
        <div style={{marginTop: 40, display: 'flex', gap: 14, fontSize: 46, color: MUTED}}>
          {'Ton iPhone en grand.'.split(' ').map((w, i) => {
            const p = progress(frame, 30 + i * 3, 44 + i * 3);
            return (
              <span key={i} style={{display: 'inline-block', opacity: p, transform: `translateY(${(1 - p) * 18}px)`}}>
                {w}
              </span>
            );
          })}
        </div>
        <div
          style={{
            marginTop: 28,
            height: 3,
            width: lerp(0, 180, progress(frame, 34, 54, easeInOut)),
            borderRadius: 2,
            background: ACCENT,
          }}
        />
      </AbsoluteFill>
    </Fade>
  );
};

/* ------------------------------------------------------------------ icons */

const stroke = {fill: 'none', stroke: '#fff', strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round'};

/** AirPlay-style icon: screen outline + triangle (same idea as the AirGlass logo). */
const AirPlayIcon = ({size = 34, color = '#fff'}) => (
  <svg width={size} height={size} viewBox="0 0 24 24">
    <path
      d="M8 17 H6 A2.5 2.5 0 0 1 3.5 14.5 V6.5 A2.5 2.5 0 0 1 6 4 H18 A2.5 2.5 0 0 1 20.5 6.5 V14.5 A2.5 2.5 0 0 1 18 17 H16"
      {...stroke}
      stroke={color}
    />
    <path d="M12 14 L17 20.5 H7 Z" fill={color} stroke={color} strokeWidth="1.2" strokeLinejoin="round" />
  </svg>
);

const SunIcon = ({color}) => (
  <svg width="28" height="28" viewBox="0 0 24 24">
    <circle cx="12" cy="12" r="4" fill={color} />
    {[0, 1, 2, 3, 4, 5, 6, 7].map((i) => {
      const a = (i * Math.PI) / 4;
      return (
        <line
          key={i}
          x1={12 + Math.cos(a) * 7}
          y1={12 + Math.sin(a) * 7}
          x2={12 + Math.cos(a) * 9.5}
          y2={12 + Math.sin(a) * 9.5}
          stroke={color}
          strokeWidth="2"
          strokeLinecap="round"
        />
      );
    })}
  </svg>
);

const SpeakerIcon = ({color}) => (
  <svg width="28" height="28" viewBox="0 0 24 24">
    <path d="M4 9.5 H8 L13 5.5 V18.5 L8 14.5 H4 Z" fill={color} />
    <path d="M16 9 A4.5 4.5 0 0 1 16 15" fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" />
  </svg>
);

/* ------------------------------------------------------------------ iPhone */

const PHONE_W = 418;
const PHONE_H = 872;
const SCREEN_W = 390;
const SCREEN_H = 844;

const SideButton = ({side, top, height}) => (
  <div
    style={{
      position: 'absolute',
      top,
      height,
      width: 5,
      [side]: -4,
      borderRadius: side === 'left' ? '3px 0 0 3px' : '0 3px 3px 0',
      background: `linear-gradient(${side === 'left' ? 90 : 270}deg, #9a9ea8, #3a3d44)`,
    }}
  />
);

const StatusBar = () => (
  <div style={{position: 'absolute', top: 0, left: 0, right: 0, height: 54, color: '#fff'}}>
    <div style={{position: 'absolute', left: 52, top: 15, fontSize: 18, fontWeight: 600}}>9:41</div>
    <svg style={{position: 'absolute', right: 34, top: 20}} width="80" height="13" viewBox="0 0 80 13">
      {[0, 1, 2, 3].map((i) => (
        <rect key={i} x={i * 5} y={9 - i * 3} width="3.2" height={4 + i * 3} rx="1" fill="#fff" />
      ))}
      <path d="M27 4.2 A11 11 0 0 1 43 4.2" fill="none" stroke="#fff" strokeWidth="2" strokeLinecap="round" />
      <path d="M30 7.4 A6.6 6.6 0 0 1 40 7.4" fill="none" stroke="#fff" strokeWidth="2" strokeLinecap="round" />
      <circle cx="35" cy="11" r="1.6" fill="#fff" />
      <rect x="50" y="0.5" width="25" height="12" rx="3.8" fill="none" stroke="rgba(255,255,255,0.4)" />
      <rect x="52" y="2.5" width="21" height="8" rx="2.2" fill="#fff" />
      <rect x="76.3" y="4.5" width="1.7" height="4" rx="0.8" fill="rgba(255,255,255,0.45)" />
    </svg>
  </div>
);

/** iPhone drawn in CSS: titanium frame, side buttons, Dynamic Island, status bar, home indicator. */
const Phone = ({statusOpacity = 1, children}) => (
  <div style={{position: 'relative', width: PHONE_W, height: PHONE_H}}>
    <SideButton side="left" top={128} height={34} />
    <SideButton side="left" top={192} height={62} />
    <SideButton side="left" top={268} height={62} />
    <SideButton side="right" top={218} height={98} />
    <div
      style={{
        position: 'absolute',
        inset: 0,
        borderRadius: 68,
        background:
          'linear-gradient(145deg, #9a9ea8 0%, #4c4f57 18%, #2a2c32 50%, #5a5d66 82%, #8d919a 100%)',
        boxShadow: '0 50px 110px rgba(0,0,0,0.6), inset 0 0 0 1px rgba(255,255,255,0.18)',
      }}
    />
    <div style={{position: 'absolute', inset: 4, borderRadius: 64, background: '#040405'}} />
    <div
      style={{
        position: 'absolute',
        left: 14,
        top: 14,
        width: SCREEN_W,
        height: SCREEN_H,
        borderRadius: 54,
        overflow: 'hidden',
        background: '#000',
      }}
    >
      {children}
      <div style={{opacity: statusOpacity}}>
        <StatusBar />
      </div>
      <div
        style={{
          position: 'absolute',
          left: (SCREEN_W - 122) / 2,
          top: 11,
          width: 122,
          height: 35,
          borderRadius: 18,
          background: '#000',
        }}
      >
        <div
          style={{
            position: 'absolute',
            right: 14,
            top: 11,
            width: 12,
            height: 12,
            borderRadius: 6,
            background: 'radial-gradient(circle at 35% 35%, #2a3350, #07080c 70%)',
          }}
        />
      </div>
      <div
        style={{
          position: 'absolute',
          left: '50%',
          bottom: 8,
          width: 134,
          height: 5,
          marginLeft: -67,
          borderRadius: 3,
          background: '#fff',
          opacity: 0.8,
        }}
      />
      <div
        style={{
          position: 'absolute',
          inset: 0,
          background: 'linear-gradient(120deg, rgba(255,255,255,0.07) 0%, rgba(255,255,255,0) 35%)',
        }}
      />
    </div>
  </div>
);

/* ------------------------------------------------------------------ Control Center */

const CC_COL = 76;
const CC_GAP = 14;
const CC_X = 22;
const CC_Y = 112;

const slot = (c, r, w = 1, h = 1) => ({
  position: 'absolute',
  left: CC_X + c * (CC_COL + CC_GAP),
  top: CC_Y + r * (CC_COL + CC_GAP),
  width: w * CC_COL + (w - 1) * CC_GAP,
  height: h * CC_COL + (h - 1) * CC_GAP,
  boxSizing: 'border-box',
  borderRadius: 26,
  background: 'rgba(130,134,152,0.34)',
});

const centered = {display: 'flex', alignItems: 'center', justifyContent: 'center'};

const Round = ({left, top, bg, children}) => (
  <div style={{position: 'absolute', left, top, width: 64, height: 64, borderRadius: 32, background: bg, ...centered}}>
    {children}
  </div>
);

const ControlCenter = ({press}) => {
  const mirrorBg = `rgba(${Math.round(lerp(130, 255, press))},${Math.round(lerp(134, 255, press))},${Math.round(
    lerp(152, 255, press)
  )},${lerp(0.34, 0.95, press).toFixed(3)})`;
  const mirrorInk = press > 0.5 ? BG : '#fff';

  return (
    <AbsoluteFill style={{background: 'linear-gradient(170deg, #1D2F5E 0%, #121B38 48%, #241A3F 100%)'}}>
      {/* Connectivity */}
      <div style={slot(0, 0, 2, 2)}>
        <Round left={12} top={12} bg="rgba(255,255,255,0.18)">
          <svg width="28" height="28" viewBox="0 0 24 24">
            <path
              d="M12 2 L14 9 L22 14 L22 16 L14 13.5 L13 20 L16 22 L16 23 L12 22 L8 23 L8 22 L11 20 L10 13.5 L2 16 L2 14 L10 9 Z"
              fill="#fff"
            />
          </svg>
        </Round>
        <Round left={90} top={12} bg="#30D158">
          <svg width="28" height="28" viewBox="0 0 24 24">
            {[5, 9, 13, 17].map((h, i) => (
              <rect key={h} x={3 + i * 5} y={20 - h} width="3" height={h} rx="1" fill="#fff" />
            ))}
          </svg>
        </Round>
        <Round left={12} top={90} bg="#0A84FF">
          <svg width="28" height="28" viewBox="0 0 24 24">
            <path d="M3 9 A13 13 0 0 1 21 9" {...stroke} />
            <path d="M6.5 12.7 A8 8 0 0 1 17.5 12.7" {...stroke} />
            <path d="M9.8 16.2 A3.5 3.5 0 0 1 14.2 16.2" {...stroke} />
            <circle cx="12" cy="19.5" r="1.4" fill="#fff" />
          </svg>
        </Round>
        <Round left={90} top={90} bg="#0A84FF">
          <svg width="28" height="28" viewBox="0 0 24 24">
            <path d="M7 7.5 L17 16.5 L12 21 L12 3 L17 7.5 L7 16.5" {...stroke} />
          </svg>
        </Round>
      </div>

      {/* Media */}
      <div style={slot(2, 0, 2, 2)}>
        <div style={{position: 'absolute', left: 18, top: 18, fontSize: 15, fontWeight: 600, color: 'rgba(255,255,255,0.6)'}}>
          Lecture inactive
        </div>
        <div
          style={{
            position: 'absolute',
            left: 0,
            right: 0,
            bottom: 18,
            display: 'flex',
            justifyContent: 'space-around',
            padding: '0 18px',
            opacity: 0.55,
          }}
        >
          <svg width="26" height="26" viewBox="0 0 24 24">
            <path d="M20 6 L11 12 L20 18 Z" fill="#fff" />
            <rect x="5" y="6" width="2.5" height="12" rx="1" fill="#fff" />
          </svg>
          <svg width="26" height="26" viewBox="0 0 24 24">
            <path d="M7 5 L19 12 L7 19 Z" fill="#fff" />
          </svg>
          <svg width="26" height="26" viewBox="0 0 24 24">
            <path d="M4 6 L13 12 L4 18 Z" fill="#fff" />
            <rect x="16.5" y="6" width="2.5" height="12" rx="1" fill="#fff" />
          </svg>
        </div>
      </div>

      {/* Orientation lock */}
      <div style={{...slot(0, 2), ...centered}}>
        <svg width="30" height="30" viewBox="0 0 24 24">
          <rect x="6.5" y="11" width="11" height="8.5" rx="2" {...stroke} />
          <path d="M9 11 V8.5 A3 3 0 0 1 15 8.5 V11" {...stroke} />
        </svg>
      </div>

      {/* Screen mirroring */}
      <div style={{...slot(1, 2), ...centered, background: mirrorBg, transform: `scale(${1 - 0.07 * press})`}}>
        <AirPlayIcon size={38} color={mirrorInk} />
      </div>

      {/* Focus */}
      <div style={{...slot(0, 3, 2, 1), display: 'flex', alignItems: 'center', gap: 12, padding: '0 20px'}}>
        <svg width="28" height="28" viewBox="0 0 24 24">
          <path d="M20 14.5 A8 8 0 1 1 9.5 4 A6.5 6.5 0 0 0 20 14.5 Z" {...stroke} />
        </svg>
        <div style={{fontSize: 17, fontWeight: 600}}>Concentration</div>
      </div>

      {/* Sliders */}
      <div style={{...slot(2, 2, 1, 2), overflow: 'hidden'}}>
        <div style={{position: 'absolute', left: 0, right: 0, bottom: 0, height: '55%', background: '#fff'}} />
        <div style={{position: 'absolute', left: 0, right: 0, bottom: 18, ...centered}}>
          <SunIcon color="#5b5f6b" />
        </div>
      </div>
      <div style={{...slot(3, 2, 1, 2), overflow: 'hidden'}}>
        <div style={{position: 'absolute', left: 0, right: 0, bottom: 0, height: '38%', background: '#fff'}} />
        <div style={{position: 'absolute', left: 0, right: 0, bottom: 18, ...centered}}>
          <SpeakerIcon color="#5b5f6b" />
        </div>
      </div>

      {/* Shortcuts */}
      <div style={{...slot(0, 4), ...centered}}>
        <svg width="30" height="30" viewBox="0 0 24 24">
          <path d="M8 3 H16 V8 L14 11 V21 H10 V11 L8 8 Z" {...stroke} />
        </svg>
      </div>
      <div style={{...slot(1, 4), ...centered}}>
        <svg width="30" height="30" viewBox="0 0 24 24">
          <circle cx="12" cy="13.5" r="8" {...stroke} />
          <path d="M12 13.5 V9.5 M10 3 H14" {...stroke} />
        </svg>
      </div>
      <div style={{...slot(2, 4), ...centered}}>
        <svg width="30" height="30" viewBox="0 0 24 24">
          <rect x="5" y="3" width="14" height="18" rx="3" {...stroke} />
          <rect x="8" y="6" width="8" height="3" rx="1" {...stroke} />
          <circle cx="9.5" cy="13" r="0.9" fill="#fff" />
          <circle cx="14.5" cy="13" r="0.9" fill="#fff" />
          <circle cx="9.5" cy="17" r="0.9" fill="#fff" />
          <circle cx="14.5" cy="17" r="0.9" fill="#fff" />
        </svg>
      </div>
      <div style={{...slot(3, 4), ...centered}}>
        <svg width="30" height="30" viewBox="0 0 24 24">
          <rect x="3" y="7" width="18" height="13" rx="3" {...stroke} />
          <circle cx="12" cy="13.5" r="3.5" {...stroke} />
          <path d="M8 7 L9.5 4 H14.5 L16 7" {...stroke} />
        </svg>
      </div>
    </AbsoluteFill>
  );
};

const SheetRow = ({name, sub, highlight = 0, spin = 0, check = 0, frame = 0}) => (
  <div
    style={{
      height: 72,
      margin: '0 10px',
      padding: '0 14px',
      boxSizing: 'border-box',
      borderRadius: 22,
      display: 'flex',
      alignItems: 'center',
      gap: 14,
      background: `rgba(61,139,255,${(0.3 * highlight).toFixed(3)})`,
    }}
  >
    <div style={{width: 44, height: 44, borderRadius: 22, background: 'rgba(255,255,255,0.14)', ...centered}}>
      <AirPlayIcon size={26} />
    </div>
    <div style={{flex: 1}}>
      <div style={{fontSize: 20, fontWeight: 600}}>{name}</div>
      <div style={{fontSize: 14, color: MUTED, marginTop: 2}}>{sub}</div>
    </div>
    <div style={{width: 28, height: 28, position: 'relative'}}>
      <svg
        width="28"
        height="28"
        viewBox="0 0 28 28"
        style={{position: 'absolute', inset: 0, opacity: spin * (1 - check), transform: `rotate(${frame * 14}deg)`}}
      >
        <circle cx="14" cy="14" r="10" fill="none" stroke="#fff" strokeWidth="3" strokeDasharray="22 42" strokeLinecap="round" />
      </svg>
      <svg width="28" height="28" viewBox="0 0 28 28" style={{position: 'absolute', inset: 0, opacity: check}}>
        <circle cx="14" cy="14" r="13" fill="#0A84FF" />
        <path d="M8.5 14.5 L12.5 18.5 L19.5 10" fill="none" stroke="#fff" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    </div>
  </div>
);

const MirrorSheet = ({p, tap, spin, check, frame}) => (
  <div style={{position: 'absolute', inset: 0, opacity: p}}>
    <div style={{position: 'absolute', inset: 0, background: 'rgba(0,0,0,0.5)'}} />
    <div
      style={{
        position: 'absolute',
        left: 28,
        right: 28,
        top: 250,
        height: 250,
        borderRadius: 36,
        background: 'rgba(44,46,58,0.94)',
        border: '1px solid rgba(255,255,255,0.1)',
        boxSizing: 'border-box',
        overflow: 'hidden',
        transform: `scale(${0.9 + 0.1 * p})`,
      }}
    >
      <div style={{height: 70, boxSizing: 'border-box', paddingTop: 24, textAlign: 'center', fontSize: 21, fontWeight: 600}}>
        Recopie de l’écran
      </div>
      <SheetRow name="Apple TV" sub="Salon" />
      <SheetRow
        name="AirGlass"
        sub={check > 0.5 ? 'Connecté' : spin > 0 ? 'Connexion…' : 'Windows'}
        highlight={tap}
        spin={spin}
        check={check}
        frame={frame}
      />
    </div>
  </div>
);

const Finger = ({x, y, opacity, press}) => (
  <div
    style={{
      position: 'absolute',
      left: x - 30,
      top: y - 30,
      width: 60,
      height: 60,
      borderRadius: 30,
      boxSizing: 'border-box',
      background: 'rgba(255,255,255,0.3)',
      border: '2px solid rgba(255,255,255,0.65)',
      opacity,
      transform: `scale(${1 - 0.18 * press})`,
    }}
  />
);

/* ------------------------------------------------------------------ Game scene (what gets mirrored) */

const GAME_SRC = 'game.mp4';
const GAME_FRAMES = 360; // 12 s @ 30 fps, looped

/** Real gameplay (0 A.D., CC BY-SA 4.0) cropped to fill the target box. */
const GameScene = ({width, height}) => (
  <div style={{position: 'relative', width, height, overflow: 'hidden', background: '#000'}}>
    <Loop durationInFrames={GAME_FRAMES}>
      <OffthreadVideo
        src={staticFile(GAME_SRC)}
        muted
        style={{width, height, objectFit: 'cover', display: 'block'}}
      />
    </Loop>
  </div>
);

/* ------------------------------------------------------------------ PC window */

const TrafficDot = ({color, marginLeft = 0}) => (
  <div style={{width: 14, height: 14, borderRadius: 7, background: color, marginLeft}} />
);

const AppWindow = ({width, height, left, top, children}) => (
  <div
    style={{
      position: 'absolute',
      left,
      top,
      width,
      height,
      background: BG,
      border: `1px solid ${BORDER}`,
      borderRadius: 14,
      overflow: 'hidden',
      boxShadow: '0 40px 120px rgba(0,0,0,0.55)',
      boxSizing: 'border-box',
    }}
  >
    <div style={{position: 'relative', height: 40}}>
      <div style={{position: 'absolute', right: 16, top: 13, display: 'flex'}}>
        <TrafficDot color="#FEBC2E" />
        <TrafficDot color="#28C840" marginLeft={8} />
        <TrafficDot color="#FF5F57" marginLeft={8} />
      </div>
      <div
        style={{
          position: 'absolute',
          inset: 0,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          gap: 8,
          color: MUTED,
          fontSize: 15,
          fontWeight: 600,
        }}
      >
        <Logo id="logo-window" size={18} />
        AirGlass
      </div>
    </div>
    <div style={{position: 'absolute', top: 40, left: 0, right: 0, bottom: 0, background: '#000'}}>{children}</div>
  </div>
);

/* ------------------------------------------------------------------ Story: iPhone -> mirroring -> rotation */

const STEP_ROWS = ['Centre de contrôle', 'Recopie de l\u2019écran', 'Choisis ton PC'];
const WIN_X = 1270;

const Story = () => {
  const f = useCurrentFrame();

  // Phone choreography.
  const enter = progress(f, 0, 40);
  const move = progress(f, 180, 225, easeInOut);
  const rot = progress(f, 270, 330, easeInOut);
  const phoneX = lerp(1330, 360, move);
  const phoneScale = lerp(1, 0.68, move);
  const floatY = Math.sin(f / 20) * 7 * enter;

  // On-screen UI states.
  const gameIn = progress(f, 146, 172);
  const ccOpacity = 1 - gameIn;
  const landscapeMix = interpolate(rot, [0.45, 0.6], [0, 1], clamp);
  const tap1 = interpolate(f, [36, 45, 54], [0, 1, 0], clamp);
  const tap2 = interpolate(f, [98, 105, 114], [0, 1, 0], clamp);
  const finger1 = interpolate(f, [30, 38, 52, 58], [0, 1, 1, 0], clamp);
  const finger2 = interpolate(f, [92, 99, 112, 118], [0, 1, 1, 0], clamp);
  const sheet = progress(f, 52, 72) * (1 - progress(f, 146, 160));
  const spin = progress(f, 105, 110, (x) => x);
  const check = progress(f, 126, 134);

  // PC window follows the video ratio.
  const winIn = progress(f, 200, 240);
  const winW = lerp(387, 1040, rot);
  const winH = lerp(880, 520, rot);
  const winLeft = WIN_X - winW / 2;
  const winTop = 540 - winH / 2;
  const sizeLabel = rot < 0.5 ? '498 × 1080' : '1920 × 886';

  const capA = progress(f, 215, 245) * (1 - progress(f, 285, 300));
  const capB = progress(f, 300, 330);

  const focus = [
    1 - progress(f, 44, 56),
    progress(f, 44, 56) * (1 - progress(f, 106, 118)),
    progress(f, 106, 118),
  ];

  return (
    <Fade duration={STORY}>
      <AbsoluteFill>
        {/* Steps */}
        <div style={{position: 'absolute', left: 140, top: 250, width: 900, opacity: 1 - progress(f, 166, 186)}}>
          <div
            style={{
              fontSize: 44,
              color: MUTED,
              fontWeight: 500,
              opacity: progress(f, 4, 28),
              marginBottom: 30,
            }}
          >
            Sur l'iPhone
          </div>
          {STEP_ROWS.map((label, i) => {
            const appear = progress(f, 10 + i * 8, 40 + i * 8);
            return (
              <div
                key={label}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  height: 130,
                  borderTop: `1px solid ${BORDER}`,
                  opacity: appear * (0.3 + 0.7 * focus[i]),
                  transform: `translateY(${(1 - appear) * 40}px)`,
                }}
              >
                <div
                  style={{
                    fontSize: 76,
                    fontWeight: 600,
                    letterSpacing: '-0.02em',
                    color: i === STEP_ROWS.length - 1 ? ACCENT : TEXT,
                  }}
                >
                  {label}
                </div>
                <div style={{fontSize: 30, color: MUTED, fontVariantNumeric: 'tabular-nums'}}>{i + 1}</div>
              </div>
            );
          })}
        </div>

        {/* iPhone */}
        <div
          style={{
            position: 'absolute',
            left: phoneX - PHONE_W / 2,
            top: 540 - PHONE_H / 2,
            width: PHONE_W,
            height: PHONE_H,
            opacity: enter,
            transform: `translateY(${(1 - enter) * 120 + floatY}px) scale(${phoneScale}) rotate(${-90 * rot}deg)`,
          }}
        >
          <Phone statusOpacity={ccOpacity}>
            <div style={{position: 'absolute', inset: 0, opacity: ccOpacity}}>
              <ControlCenter press={tap1} />
              <MirrorSheet p={sheet} tap={tap2} spin={spin} check={check} frame={f} />
              <Finger x={150} y={330} opacity={finger1} press={tap1} />
              <Finger x={195} y={428} opacity={finger2} press={tap2} />
            </div>
            <div style={{position: 'absolute', inset: 0, opacity: gameIn * (1 - landscapeMix)}}>
              <GameScene width={SCREEN_W} height={SCREEN_H} />
            </div>
            <div
              style={{
                position: 'absolute',
                left: (SCREEN_W - SCREEN_H) / 2,
                top: (SCREEN_H - SCREEN_W) / 2,
                width: SCREEN_H,
                height: SCREEN_W,
                transform: 'rotate(90deg)',
                opacity: gameIn * landscapeMix,
              }}
            >
              <GameScene width={SCREEN_H} height={SCREEN_W} />
            </div>
          </Phone>
        </div>

        {/* PC window */}
        <div style={{opacity: winIn, transform: `translateY(${(1 - winIn) * 40 - floatY * 0.6}px)`}}>
          <svg width={1920} height={1080} style={{position: 'absolute', left: 0, top: 0, overflow: 'visible'}}>
            <line
              x1={lerp(520, 664, rot)}
              y1={540}
              x2={winLeft - 20}
              y2={540}
              stroke={ACCENT}
              strokeWidth={3}
              strokeLinecap="round"
              strokeDasharray="4 14"
              strokeDashoffset={-f * 1.5}
              opacity={0.8 * progress(f, 205, 235)}
            />
          </svg>
          <AppWindow width={winW} height={winH} left={winLeft} top={winTop}>
            <GameScene width={Math.round(winW) - 2} height={Math.round(winH) - 42} />
          </AppWindow>
          <div
            style={{
              position: 'absolute',
              left: WIN_X - 200,
              width: 400,
              top: winTop + winH + 24,
              textAlign: 'center',
              fontSize: 26,
              color: MUTED,
              fontVariantNumeric: 'tabular-nums',
            }}
          >
            {sizeLabel}
          </div>
        </div>

        {/* Captions */}
        <div style={{position: 'absolute', left: 120, top: 892, fontSize: 46, fontWeight: 600, lineHeight: 1.15}}>
          <div style={{position: 'absolute', whiteSpace: 'nowrap', opacity: capA, transform: `translateY(${(1 - capA) * 18}px)`}}>
            Portrait : fenêtre haute.
          </div>
          <div style={{position: 'absolute', whiteSpace: 'nowrap', opacity: capB, transform: `translateY(${(1 - capB) * 18}px)`}}>
            Tu pivotes. Elle suit.
          </div>
        </div>
      </AbsoluteFill>
    </Fade>
  );
};

const Outro = () => {
  const frame = useCurrentFrame();
  const logo = progress(frame, 0, 30);
  const word = progress(frame, 12, 42);
  const tag = progress(frame, 34, 60);

  return (
    <Fade duration={OUTRO} fadeOut={1}>
      <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center'}}>
        <div style={{position: 'relative'}}>
          {[0, 1].map((i) => {
            const p = progress(frame, 10 + i * 14, 70 + i * 14);
            return (
              <div
                key={i}
                style={{
                  position: 'absolute',
                  left: '50%',
                  top: '50%',
                  width: 200,
                  height: 200,
                  marginLeft: -100,
                  marginTop: -100,
                  borderRadius: 48,
                  border: `2px solid ${ACCENT}`,
                  opacity: (1 - p) * 0.6 * logo,
                  transform: `scale(${1 + p * 1.4})`,
                }}
              />
            );
          })}
          <div style={{opacity: logo, transform: `scale(${0.9 + 0.1 * logo}) rotate(${(1 - logo) * -12}deg)`}}>
            <Logo id="logo-outro" size={200} />
          </div>
        </div>
        <div
          style={{
            marginTop: 40,
            fontSize: 132,
            fontWeight: 600,
            letterSpacing: '-0.03em',
            lineHeight: 1.1,
            opacity: word,
            transform: `translateY(${(1 - word) * 24}px)`,
          }}
        >
          AirGlass
        </div>
        <div style={{marginTop: 16, fontSize: 42, color: MUTED, opacity: tag}}>Recopie AirPlay sur Windows.</div>
      </AbsoluteFill>
    </Fade>
  );
};

export const AirGlassPromo = () => (
  <AbsoluteFill style={{background: BG, color: TEXT, fontFamily: FONT}}>
    <Audio src={staticFile('soundtrack.wav')} />
    <Backdrop />
    <Sequence from={0} durationInFrames={INTRO}>
      <Intro />
    </Sequence>
    <Sequence from={INTRO} durationInFrames={STORY}>
      <Story />
    </Sequence>
    <Sequence from={INTRO + STORY} durationInFrames={OUTRO}>
      <Outro />
    </Sequence>
  </AbsoluteFill>
);
