using System;
using System.Collections.Generic;
using SimManagementLib.Api;
using SimManagementLib.BusinessAdvisor;
using SimManagementLib.Pojo;
using SimManagementLib.Tool;
using UnityEngine;
using Verse;

namespace SimManagementLib.SimDialog
{
    //把顾问开关、模型、人格和头像收纳到统一 LLM 设置。
    public partial class Dialog_CustomerReviewAiSettings
    {
        private string advisorProfilePath = "";
        private string advisorAvatarPath = "";
        private string advisorRoundLimitBuffer;
        private float advisorSettingsHeight = 1250f;

        //按实际文本高度绘制顾问配置，不把人格文件当作接口或执行权限配置。
        private void DrawAdvisorSettings(Rect rect, SimManagementLibSettings settings)
        {
            ResetText();
            float y = 0f, line = Text.LineHeightOf(GameFont.Small);
            bool previous = settings.advisorEnabled;
            DrawCheckbox(new Rect(0f, y, rect.width, line + 8f), A("Enable"), ref settings.advisorEnabled, A("EnableHint"));
            if (previous != settings.advisorEnabled) SimShopUiApi.RequestRefresh();
            y += line + 18f;
            string hint = A("EnableHint");
            float hintHeight = Text.CalcHeight(hint, rect.width - 10f);
            Widgets.Label(new Rect(0f, y, rect.width - 10f, hintHeight), hint); y += hintHeight + 12f;
            DrawTextField(rect.width, ref y, A("Model"), ref settings.advisorModel, false);
            DrawAdvisorRoundLimit(rect.width, ref y, settings);
            string approvalLabel = A("AutoApprove");
            float approvalHeight = Text.CalcHeight(approvalLabel, rect.width - 40f) + 8f;
            DrawCheckbox(new Rect(0f, y, rect.width, approvalHeight), approvalLabel, ref settings.advisorAutoApprove, A("AutoApproveHint"));
            y += approvalHeight + 6f;
            float approvalHintHeight = Text.CalcHeight(A("AutoApproveHint"), rect.width - 10f);
            Widgets.Label(new Rect(0f, y, rect.width - 10f, approvalHintHeight), A("AutoApproveHint"));
            y += approvalHintHeight + 14f;
            DrawCheckbox(new Rect(0f, y, rect.width, line + 8f), A("SeparateConnection"), ref settings.advisorSeparateConnection, A("SeparateHint"));
            y += line + 16f;
            if (settings.advisorSeparateConnection)
            {
                if (Widgets.ButtonText(new Rect(0f, y, 260f, line + 12f), settings.advisorProvider.ToString()))
                    Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                    {
                        new FloatMenuOption("OpenAI compatible", () => settings.advisorProvider = SimLlmProvider.OpenAICompatible),
                        new FloatMenuOption("Anthropic", () => settings.advisorProvider = SimLlmProvider.Anthropic)
                    }));
                y += line + 22f;
                if (settings.advisorProvider == SimLlmProvider.OpenAICompatible)
                    DrawTextField(rect.width, ref y, "Base URL", ref settings.advisorBaseUrl, false);
                Widgets.Label(new Rect(0f, y, rect.width, line), "API Key"); y += line + 4f;
                settings.advisorApiKey = GUI.PasswordField(new Rect(0f, y, rect.width - 10f, line + 12f), settings.advisorApiKey ?? "", '*');
                y += line + 24f;
            }
            DrawAdvisorOutputSettings(rect.width, ref y, settings);
            DrawTextField(rect.width, ref y, A("Name"), ref settings.advisorName, false);
            DrawTextArea(rect.width, ref y, A("Personality"), ref settings.advisorPersonality, 110f);
            DrawTextArea(rect.width, ref y, A("Expectations"), ref settings.advisorExpectations, 110f);
            settings.advisorName = Limit(settings.advisorName, 80);
            settings.advisorPersonality = Limit(settings.advisorPersonality, 8000);
            settings.advisorExpectations = Limit(settings.advisorExpectations, 8000);
            DrawImportRow(rect.width, ref y, A("ProfilePath"), ref advisorProfilePath, () => AdvisorProfile.Import(advisorProfilePath, settings));
            DrawImportRow(rect.width, ref y, A("AvatarPath"), ref advisorAvatarPath, () =>
            {
                if (!SignImageLibrary.TryImportFromPath(advisorAvatarPath.Trim().Trim('"'), out var record, out string error))
                    throw new InvalidOperationException(error);
                settings.advisorAvatarId = record.imageId;
            });
            Texture2D avatar = string.IsNullOrEmpty(settings.advisorAvatarId) ? null : SignTextureCache.GetTexture(settings.advisorAvatarId);
            if (avatar != null) GUI.DrawTexture(new Rect(0f, y, 64f, 64f), avatar, ScaleMode.ScaleToFit);
            if (Widgets.ButtonText(new Rect(76f, y + 16f, 150f, line + 12f), A("ClearAvatar"))) settings.advisorAvatarId = "";
            y += 78f;
            string note = A("CacheHint");
            float noteHeight = Text.CalcHeight(note, rect.width - 10f);
            Widgets.Label(new Rect(0f, y, rect.width - 10f, noteHeight), note);
            advisorSettingsHeight = y + noteHeight + 12f;
        }

        //按实际供应商显示输出选项，不把兼容接口参数发给 Anthropic Messages。
        private void DrawAdvisorOutputSettings(float width, ref float y, SimManagementLibSettings settings)
        {
            SimLlmProvider provider = settings.advisorSeparateConnection ? settings.advisorProvider : settings.llmProvider;
            string hint;
            if (provider == SimLlmProvider.OpenAICompatible)
            {
                string label = A("JsonMode");
                float height = Text.CalcHeight(label, width - 40f) + 8f;
                DrawCheckbox(new Rect(0f, y, width, height), label, ref settings.advisorJsonMode, A("JsonModeHint"));
                y += height + 6f;
                hint = A("JsonModeHint");
            }
            else hint = A("AnthropicOutputHint");
            float hintHeight = Text.CalcHeight(hint, width - 10f);
            Widgets.Label(new Rect(0f, y, width - 10f, hintHeight), hint);
            y += hintHeight + 14f;
        }

        //每次玩家消息单独计数；零表示不设自动停止额度。
        private void DrawAdvisorRoundLimit(float width, ref float y, SimManagementLibSettings settings)
        {
            float labelHeight = Text.CalcHeight(A("RoundLimit"), width - 10f);
            Widgets.Label(new Rect(0f, y, width - 10f, labelHeight), A("RoundLimit"));
            y += labelHeight + 4f;
            float fieldHeight = Mathf.Max(32f, Text.LineHeight + 10f);
            Widgets.TextFieldNumeric(new Rect(0f, y, 160f, fieldHeight), ref settings.advisorMaxRounds, ref advisorRoundLimitBuffer, 0f);
            y += fieldHeight + 6f;
            string hint = A("RoundLimitHint");
            float hintHeight = Text.CalcHeight(hint, width - 10f);
            Widgets.Label(new Rect(0f, y, width - 10f, hintHeight), hint);
            y += hintHeight + 14f;
        }

        //导入操作只在玩家点击时读取填写的路径，并显示明确错误。
        private void DrawImportRow(float width, ref float y, string label, ref string path, Action import)
        {
            Text.Font = GameFont.Small;
            float h = Text.CalcHeight(label, width - 10f);
            Widgets.Label(new Rect(0f, y, width - 10f, h), label); y += h + 4f;
            float button = Mathf.Max(90f, Text.CalcSize(A("Import")).x + 24f);
            path = Widgets.TextField(new Rect(0f, y, width - button - 20f, 34f), path);
            if (Widgets.ButtonText(new Rect(width - button - 10f, y, button, 34f), A("Import")))
            {
                try { import(); connectionStatus = A("Imported"); }
                catch (Exception ex) { connectionStatus = ex.Message; }
            }
            y += 46f;
        }

        //统一顾问设置的翻译键。
        private static string A(string key) => SimTranslation.T("RSMF.Business.Advisor." + key);
        //限制人格提示长度，避免误粘贴巨型文件。
        private static string Limit(string text, int max) => string.IsNullOrEmpty(text) ? "" : text.Substring(0, Math.Min(text.Length, max));
    }
}
