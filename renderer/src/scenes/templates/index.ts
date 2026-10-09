import type React from "react";
import type { SceneType } from "../../schemas/videoPlan";
import { AnatomyTemplate } from "./AnatomyTemplate";
import { ExerciseDemoTemplate } from "./ExerciseDemoTemplate";
import { GenericTemplate } from "./GenericTemplate";
import { HookTemplate } from "./HookTemplate";
import { ListTemplate } from "./ListTemplate";
import { VerdictTemplate } from "./VerdictTemplate";
import { ProgressTemplate } from "./ProgressTemplate";
import type { TemplateProps } from "./shared";

// One template per scene type. Exhaustive by type (Day 9 pattern): a scene type
// added to the schema without a template here doesn't compile.
// Comparison and MythVsFact share the wrong -> right "verdict" (red X, then green check);
// Recovery shares List's one-tip-at-a-time wall (unnumbered).
export const TEMPLATE_BY_SCENE_TYPE: Record<SceneType, React.FC<TemplateProps>> = {
  Hook: HookTemplate,
  ExerciseDemo: ExerciseDemoTemplate,
  Comparison: VerdictTemplate,
  MythVsFact: VerdictTemplate,
  AnatomyHighlight: AnatomyTemplate,
  Progress: ProgressTemplate,
  List: ListTemplate,
  Recovery: ListTemplate,
  Outro: GenericTemplate,
};

export type { TemplateProps } from "./shared";
