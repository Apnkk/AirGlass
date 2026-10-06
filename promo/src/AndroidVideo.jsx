import React from 'react';
import {AbsoluteFill, Audio, Sequence, interpolate, staticFile, useCurrentFrame} from 'remotion';
import {
  INTRO,
  OUTRO,
  TOTAL_FRAMES,
  BG,
  TEXT,
  MUTED,
  ACCENT,
  BORDER,
  FONT,
  clamp,
  easeInOut,
  progress,
  lerp,
  centered,
  Backdrop,
  Fade,
  Logo,
  GameScene,
  AppWindow,
} from './Video';

// Same 19 s timeline as the iPhone video so public/soundtrack.wav stays in sync:
// story frame 45 = first tap, 105 = second tap, 126 = "connected" chime,
// 146 = mirroring starts, 180-225 = slide, 270-330 = rotation.
const STORY = 420;

const PW = 400;
const PH = 840;
const PAD = 12;
const SW = PW - 2 * PAD;
const SH = PH - 2 * PAD;
const WIN_X = 1270;
const STEP_ROWS = ['Débogage USB', 'Branche le câble', 'Autorise le PC'];

/* ------------------------------------------------------------------ phone */

const AndroidPhone = ({statusOpacity = 1, children}) => (
  <div
    style={{
      position: 'relative',
      width: PW,
      height: PH,
      boxSizing: 'border-box',
      background: '#15171C',
      border: `1px solid ${BORDER}`,
      borderRadius: 44,
      boxShadow: '0 40px 120px rgba(0,0,0,0.55)',
    }}
  >
    <div
      style={{
        position: 'absolute',
        left: PAD,
        top: PAD,
        width: SW,
        height: SH,
        borderRadius: 32,
        overflow: 'hidden',
        background: '#0E0F12',
      }}
    >
      {children}
      <div
        style={{
          position: 'absolute',
          left: SW / 2 - 8,
          top: 14,
          width: 16,
          height: 16,
          borderRadius: 8,
          background: '#000',
          zIndex: 5,
        }}
      />
      <div
        style={{
          position: 'absolute',
          left: 28,
          top: 12,
          fontSize: 16,
          fontWeight: 600,
          color: TEXT,
          opacity: statusOpacity,
          zIndex: 5,
        }}
      >
        12:30
      </div>
    </div>
  </div>
);

const SettingRow = ({label, sub, on = 0, highlight = 0}) => (
  <div
    style={{
      height: 84,
      boxSizing: 'border-box',
      padding: '0 24px',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'space-between',
      background: `rgba(61,139,255,${0.12 * highlight})`,
    }}
  >
    <div>
      <div style={{fontSize: 22, color: TEXT, fontWeight: 500}}>{label}</div>
      <div style={{fontSize: 15, color: MUTED, marginTop: 4}}>{sub}</div>
    </div>
    <div style={{position: 'relative', width: 52, height: 30, borderRadius: 15, background: '#3A3D45'}}>
      <div style={{position: 'absolute', inset: 0, borderRadius: 15, background: ACCENT, opacity: on}} />
      <div
        style={{
          position: 'absolute',
          top: 4,
          left: lerp(4, 26, on),
          width: 22,
          height: 22,
          borderRadius: 11,
          background: '#F2F4F8',
        }}
      />
    </div>
  </div>
);

const SettingsScreen = ({toggle}) => (
  <div style={{position: 'absolute', inset: 0}}>
    <div style={{padding: '64px 24px 20px', fontSize: 28, fontWeight: 600, color: TEXT, lineHeight: 1.15}}>
      Options pour les développeurs
    </div>
    <div style={{height: 1, background: BORDER}} />
    <SettingRow label="Rester allumé" sub="L'écran ne se met pas en veille" />
    <SettingRow label="Débogage USB" sub="Mode débogage quand l'USB est branché" on={toggle} highlight={toggle} />
    <SettingRow label="Débogage sans fil" sub="Désactivé" />
  </div>
);

const AuthDialog = ({p, tap}) => (
  <div style={{position: 'absolute', inset: 0, opacity: p, background: 'rgba(0,0,0,0.55)'}}>
    <div
      style={{
        position: 'absolute',
        left: 28,
        top: 283,
        width: 320,
        height: 250,
        boxSizing: 'border-box',
        padding: 24,
        borderRadius: 28,
        background: '#23262D',
        transform: `translateY(${(1 - p) * 24}px)`,
      }}
    >
      <div style={{fontSize: 23, fontWeight: 600, color: TEXT, lineHeight: 1.2}}>Autoriser le débogage USB ?</div>
      <div style={{display: 'flex', alignItems: 'center', gap: 12, marginTop: 24}}>
        <div style={{width: 22, height: 22, borderRadius: 5, background: ACCENT, flex: 'none'}} />
        <div style={{fontSize: 16, color: MUTED}}>Toujours autoriser cet ordinateur</div>
      </div>
      <div
        style={{
          position: 'absolute',
          right: 24,
          bottom: 20,
          display: 'flex',
          gap: 24,
          fontSize: 18,
          fontWeight: 600,
        }}
      >
        <div style={{color: MUTED}}>Annuler</div>
        <div style={{color: ACCENT, transform: `scale(${1 - 0.1 * tap})`}}>Autoriser</div>
      </div>
    </div>
  </div>
);

const Connected = ({p}) => (
  <div style={{position: 'absolute', inset: 0, background: '#0E0F12', opacity: p, ...centered, flexDirection: 'column'}}>
    <div
      style={{
        width: 120,
        height: 120,
        borderRadius: 60,
        background: ACCENT,
        ...centered,
        transform: `scale(${0.7 + 0.3 * p})`,
      }}
    >
      <svg width={60} height={60} viewBox="0 0 24 24" fill="none" stroke="#fff" strokeWidth={2.4} strokeLinecap="round" strokeLinejoin="round">
        <path d="M5 12.5l4.5 4.5L19 7.5" />
      </svg>
    </div>
    <div style={{marginTop: 28, fontSize: 24, fontWeight: 600, color: TEXT}}>Connecté au PC</div>
  </div>
);

const Finger = ({x, y, opacity, press}) => (
  <div
    style={{
      position: 'absolute',
      left: x - 28,
      top: y - 28,
      width: 56,
      height: 56,
      borderRadius: 28,
      background: 'rgba(255,255,255,0.3)',
      border: '2px solid rgba(255,255,255,0.55)',
      boxSizing: 'border-box',
      opacity,
      transform: `scale(${1 - 0.2 * press})`,
      zIndex: 6,
    }}
  />
);

/* ------------------------------------------------------------------ intro / story / outro */

const Intro = () => {
  const frame = useCurrentFrame();
  const draw = progress(frame, 0, 22, easeInOut);
  const pop = progress(frame, 18, 32);
  const word = progress(frame, 14, 34);

  return (
    <Fade duration={INTRO} fadeIn={1}>
      <AbsoluteFill style={{alignItems: 'center', justifyContent: 'center'}}>
        <div style={{display: 'flex', alignItems: 'center', gap: 48}}>
          <Logo id="logo-intro-android" size={220} draw={draw} pop={pop} />
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
          {'Ton Android en grand.'.split(' ').map((w, i) => {
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

const Story = () => {
  const f = useCurrentFrame();

  // Phone choreography (identical to the iPhone story).
  const enter = progress(f, 0, 40);
  const move = progress(f, 180, 225, easeInOut);
  const rot = progress(f, 270, 330, easeInOut);
  const phoneX = lerp(1330, 360, move);
  const phoneScale = lerp(1, 0.68, move);
  const floatY = Math.sin(f / 20) * 7 * enter;

  // UI states.
  const gameIn = progress(f, 146, 172);
  const uiOpacity = 1 - gameIn;
  const landscapeMix = interpolate(rot, [0.45, 0.6], [0, 1], clamp);
  const tap1 = interpolate(f, [36, 45, 54], [0, 1, 0], clamp);
  const tap2 = interpolate(f, [98, 105, 114], [0, 1, 0], clamp);
  const finger1 = interpolate(f, [30, 38, 52, 58], [0, 1, 1, 0], clamp);
  const finger2 = interpolate(f, [92, 99, 112, 118], [0, 1, 1, 0], clamp);
  const toggle = progress(f, 44, 54);
  const dialog = progress(f, 88, 100) * (1 - progress(f, 108, 120));
  const connected = progress(f, 124, 136);
  const plug = progress(f, 58, 90);
  const cableOpacity = progress(f, 56, 64) * (1 - progress(f, 140, 156));

  // PC window follows the video ratio.
  const winIn = progress(f, 200, 240);
  const winW = lerp(387, 1040, rot);
  const winH = lerp(880, 520, rot);
  const winLeft = WIN_X - winW / 2;
  const winTop = 540 - winH / 2;

  const capA = progress(f, 215, 245) * (1 - progress(f, 285, 300));
  const capB = progress(f, 300, 330);

  const focus = [
    1 - progress(f, 50, 60),
    progress(f, 50, 60) * (1 - progress(f, 100, 110)),
    progress(f, 100, 110),
  ];

  return (
    <Fade duration={STORY}>
      <AbsoluteFill>
        {/* Steps */}
        <div style={{position: 'absolute', left: 140, top: 250, width: 900, opacity: 1 - progress(f, 166, 186)}}>
          <div style={{fontSize: 44, color: MUTED, fontWeight: 500, opacity: progress(f, 4, 28), marginBottom: 30}}>
            Sur l'Android
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

        {/* Android phone */}
        <div
          style={{
            position: 'absolute',
            left: phoneX - PW / 2,
            top: 540 - PH / 2,
            width: PW,
            height: PH,
            opacity: enter,
            transform: `translateY(${(1 - enter) * 120 + floatY}px) scale(${phoneScale}) rotate(${-90 * rot}deg)`,
          }}
        >
          {/* USB cable, drawn under the phone body */}
          <div style={{position: 'absolute', inset: 0, opacity: cableOpacity}}>
            <div
              style={{
                position: 'absolute',
                left: PW / 2 - 17,
                top: PH - 30 + (1 - plug) * 160,
                width: 34,
                height: 60,
                borderRadius: 8,
                background: '#C9CCD3',
              }}
            />
            <div
              style={{
                position: 'absolute',
                left: PW / 2 - 5,
                top: PH + 30 + (1 - plug) * 160,
                width: 10,
                height: 500,
                background: '#2A2D34',
              }}
            />
          </div>

          <AndroidPhone statusOpacity={uiOpacity}>
            <div style={{position: 'absolute', inset: 0, opacity: uiOpacity}}>
              <SettingsScreen toggle={toggle} />
              <AuthDialog p={dialog} tap={tap2} />
              <Connected p={connected} />
              <Finger x={326} y={256} opacity={finger1} press={tap1} />
              <Finger x={280} y={495} opacity={finger2} press={tap2} />
            </div>
            <div style={{position: 'absolute', inset: 0, opacity: gameIn * (1 - landscapeMix)}}>
              <GameScene width={SW} height={SH} />
            </div>
            <div
              style={{
                position: 'absolute',
                left: (SW - SH) / 2,
                top: (SH - SW) / 2,
                width: SH,
                height: SW,
                transform: 'rotate(90deg)',
                opacity: gameIn * landscapeMix,
              }}
            >
              <GameScene width={SH} height={SW} />
            </div>
          </AndroidPhone>
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
            <Logo id="logo-outro-android" size={200} />
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
        <div style={{marginTop: 16, fontSize: 42, color: MUTED, opacity: tag}}>Recopie Android sur Windows.</div>
      </AbsoluteFill>
    </Fade>
  );
};

export const ANDROID_TOTAL_FRAMES = TOTAL_FRAMES;

export const AirGlassAndroidPromo = () => (
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