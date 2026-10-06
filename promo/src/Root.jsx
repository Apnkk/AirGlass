import React from 'react';
import {Composition} from 'remotion';
import {AirGlassPromo, TOTAL_FRAMES} from './Video';

export const Root = () => (
  <Composition
    id="AirGlassPromo"
    component={AirGlassPromo}
    durationInFrames={TOTAL_FRAMES}
    fps={30}
    width={1920}
    height={1080}
  />
);