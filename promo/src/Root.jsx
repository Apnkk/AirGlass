import React from 'react';
import {Composition} from 'remotion';
import {AirGlassPromo, TOTAL_FRAMES} from './Video';
import {AirGlassAndroidPromo, ANDROID_TOTAL_FRAMES} from './AndroidVideo';

export const Root = () => (
  <>
    <Composition
      id="AirGlassPromo"
      component={AirGlassPromo}
      durationInFrames={TOTAL_FRAMES}
      fps={30}
      width={1920}
      height={1080}
    />
    <Composition
      id="AirGlassAndroidPromo"
      component={AirGlassAndroidPromo}
      durationInFrames={ANDROID_TOTAL_FRAMES}
      fps={30}
      width={1920}
      height={1080}
    />
  </>
);