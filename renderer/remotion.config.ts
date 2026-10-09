/**
 * Note: When using the Node.JS APIs, the config file
 * doesn't apply. Instead, pass options directly to the APIs.
 *
 * All configuration options: https://remotion.dev/docs/config
 */

import { Config } from "@remotion/cli/config";

Config.setRspack(true);
Config.setVideoImageFormat("jpeg");
// Line art (thin black outlines, flat color) is what JPEG and H.264 blur first: the
// defaults (JPEG 80, CRF 18) left visibly soft outlines. 95 / 15 keep them crisp; a Short
// grows from ~8 to ~15 MB, which YouTube re-encodes anyway - from a cleaner source.
Config.setJpegQuality(95);
Config.setCrf(15);
Config.setOverwriteOutput(true);
