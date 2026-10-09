import { describe, expect, it } from "vitest";
import { imageArt, imageSrc, isRasterPath } from "./art";

describe("art", () => {
  it("round-trips an image URL and leaves SVG markup alone", () => {
    expect(imageSrc(imageArt("/public/assets/mascot/mascot-idle.png"))).toBe("/public/assets/mascot/mascot-idle.png");
    expect(imageSrc('<svg viewBox="0 0 10 10"></svg>')).toBeNull();
  });

  it("knows which files are raster art", () => {
    expect(isRasterPath("../mascot/mascot-idle.PNG")).toBe(true);
    expect(isRasterPath("../mascot/mascot-idle.svg")).toBe(false);
  });
});
