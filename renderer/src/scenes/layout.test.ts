import { describe, expect, it } from "vitest";
import { imageArt, imageInfo, imageSrc } from "../assets/art";
import { placeArt } from "./layout";
import { poseAt } from "./templates/poses";

// A 1000x2000 picture whose figure fills x 300..700, y 60..1940 (transparent margin around it).
const standing = { width: 1000, height: 2000, bounds: [300, 60, 700, 1940] as const };
const box = { width: 1080, height: 1245 };

describe("placeArt", () => {
  it("puts a standing figure's feet exactly on the floor and its head at the top of the box", () => {
    const p = placeArt(standing, "bottom", box);
    const s = p.width / standing.width;
    expect(p.top + 1940 * s).toBeCloseTo(box.height);   // feet on the floor line
    expect(p.top + 60 * s).toBeCloseTo(0);              // the margin no longer shrinks the figure
    expect(p.left + 500 * s).toBeCloseTo(box.width / 2); // centered on the figure, not the picture
    expect(p.originY).toBeCloseTo(1940 * s);            // motion pivots at the feet
  });

  it("hangs a hanging pose from the top of the box", () => {
    const p = placeArt(standing, "top", box);
    expect(p.top + 60 * (p.width / standing.width)).toBeCloseTo(0);
  });

  it("keeps an icon modest and in the middle", () => {
    const icon = { width: 1000, height: 1000, bounds: [0, 0, 1000, 1000] as const };
    const p = placeArt(icon, "center", box);
    expect(p.width).toBeLessThan(box.width * 0.8);
    expect(p.top + p.height / 2).toBeCloseTo(box.height * 0.45);
  });
});

describe("art string", () => {
  it("round-trips the measured size, figure bounds and anchor", () => {
    const art = imageArt("/static/assets/mascot/mascot-idle.png", { width: 1157, height: 2048, bounds: [12.4, 10, 1140, 2040], anchor: "bottom" });
    expect(imageSrc(art)).toBe("/static/assets/mascot/mascot-idle.png");
    expect(imageInfo(art)).toEqual({
      src: "/static/assets/mascot/mascot-idle.png",
      width: 1157,
      height: 2048,
      bounds: [12, 10, 1140, 2040],
      anchor: "bottom",
    });
  });

  it("has no info for unmeasured art", () => {
    expect(imageInfo(imageArt("/a.png"))).toBeNull();
    expect(imageSrc(imageArt("/a.png"))).toBe("/a.png");
  });
});

describe("poseAt", () => {
  const available = { "mascot-stop": "a", "mascot-thumbsup": "b" };

  it("turns a myth's stop into a thumbs-up when the fact lands", () => {
    expect(poseAt("MythVsFact", "mascot-stop", 10, 100, available)).toEqual({ id: "mascot-stop", swapFrame: null });
    expect(poseAt("MythVsFact", "mascot-stop", 40, 100, available)).toEqual({ id: "mascot-thumbsup", swapFrame: 40 });
  });

  it("keeps the pose when the reaction picture isn't loaded or the scene has no reaction", () => {
    expect(poseAt("MythVsFact", "mascot-stop", 80, 100, { "mascot-stop": "a" }).id).toBe("mascot-stop");
    expect(poseAt("List", "mascot-stop", 80, 100, available).id).toBe("mascot-stop");
  });
});
