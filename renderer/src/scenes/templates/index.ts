import type React from "react";
import type { SceneType } from "../../schemas/videoPlan";
import { AnatomyTemplate } from "./AnatomyTemplate";
import { ComparisonTemplate } from "./ComparisonTemplate";
import { ExerciseDemoTemplate } from "./ExerciseDemoTemplate";
import { GenericTemplate } from "./GenericTemplate";
import { HookTemplate } from "./HookTemplate";
import { ListTemplate } from "./ListTemplate";
import { MythVsFactTemplate } from "./MythVsFactTemplate";
import { ProgressTemplate } from "./ProgressTemplate";
import { RecoveryTemplate } from "./RecoveryTemplate";
import type { TemplateProps } from "./shared";

// One template per scene type. Exhaustive by type (Day 9 pattern): a scene type
// added to the schema without a template here doesn't compile.
export const TEMPLATE_BY_SCENE_TYPE: Record<SceneType, React.FC<TemplateProps>> = {
  Hook: HookTemplate,
  ExerciseDemo: ExerciseDemoTemplate,
  Comparison: ComparisonTemplate,
  MythVsFact: MythVsFactTemplate,
  AnatomyHighlight: AnatomyTemplate,
  Progress: ProgressTemplate,
  List: ListTemplate,
  Recovery: RecoveryTemplate,
  Outro: GenericTemplate,
};

export type { TemplateProps } from "./shared";
