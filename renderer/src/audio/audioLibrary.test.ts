import { describe, expect, it } from "vitest";
import { AUDIO_MIX, musicVolumeAt, resolveMusicPath, SFX_PATHS } from "./audioLibrary";

describe("musicVolumeAt", () => {
  const fps = 30;
  const duration = 900;

  it("starts silent and reaches the music level after the fade-in", () => {
    expect(musicVolumeAt(0, fps, duration)).toBe(0);
    expect(musicVolumeAt(15, fps, duration)).toBeCloseTo(AUDIO_MIX.music / 2);
    expect(musicVolumeAt(30, fps, duration)).toBeCloseTo(AUDIO_MIX.music);
  });

  it("fades out to silence exactly at the last frame", () => {
    expect(musicVolumeAt(duration - 45, fps, duration)).toBeCloseTo(AUDIO_MIX.music);
    expect(musicVolumeAt(duration, fps, duration)).toBe(0);
  });

  it("never exceeds the music level, so the voice stays dominant", () => {
    for (let frame = 0; frame <= duration; frame += 7) {
      expect(musicVolumeAt(frame, fps, duration)).toBeLessThanOrEqual(AUDIO_MIX.music);
    }
    expect(AUDIO_MIX.music).toBeLessThan(AUDIO_MIX.sfx);
    expect(AUDIO_MIX.sfx).toBeLessThan(AUDIO_MIX.voice);
  });

  it("handles videos shorter than both fades without going negative", () => {
    expect(musicVolumeAt(10, fps, 20)).toBeGreaterThanOrEqual(0);
  });
});

describe("resolveMusicPath", () => {
  it("maps known moods and falls back to upbeat", () => {
    expect(resolveMusicPath("calm")).toBe("assets/music/calm.wav");
    expect(resolveMusicPath("Calm")).toBe("assets/music/calm.wav");
    expect(resolveMusicPath(null)).toBe("assets/music/upbeat.wav");
    expect(resolveMusicPath("toString")).toBe("assets/music/upbeat.wav");
  });
});

describe("SFX_PATHS", () => {
  it("points every sfx id at a generated wav", () => {
    for (const [id, path] of Object.entries(SFX_PATHS)) {
      expect(path).toBe(`assets/sfx/${id}.wav`);
    }
  });
});
