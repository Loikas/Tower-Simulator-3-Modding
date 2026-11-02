using System;
using System.Collections.Generic;
using UnityEngine;

namespace TS3Modding.UI
{
    internal sealed class OverlayTheme:IDisposable
    {
        private readonly List<Texture2D> textures=new List<Texture2D>();
        internal GUISkin Skin {get;}
        internal GUIStyle Window,Title,Subtitle,Section,Body,Muted,Card,Primary,Secondary,Danger,Selected,Row,Chip,Metric;
        private GUIStyle goodChip,warnChip,mutedChip;
        private readonly Color ink=new Color(.89f,.93f,.97f),dim=new Color(.58f,.66f,.75f),cyan=new Color(.34f,.82f,.87f);
        internal OverlayTheme(GUISkin source)
        {
            Skin=UnityEngine.Object.Instantiate(source);Skin.hideFlags=HideFlags.HideAndDontSave;
            var panel=Texture(new Color(.055f,.075f,.105f));var surface=Texture(new Color(.09f,.12f,.16f));
            var field=Texture(new Color(.045f,.065f,.09f));var hover=Texture(new Color(.15f,.20f,.26f));
            var blue=Texture(new Color(.12f,.43f,.55f));var blueHover=Texture(new Color(.16f,.56f,.66f));
            var red=Texture(new Color(.43f,.18f,.20f));
            Body=Text(14,ink);Muted=Text(12,dim);Title=Text(23,ink,FontStyle.Bold);Subtitle=Text(12,dim);Section=Text(11,cyan,FontStyle.Bold);
            Metric=Text(22,ink,FontStyle.Bold);Chip=Text(11,cyan,FontStyle.Bold);Chip.normal.background=surface;Chip.padding=new RectOffset(9,9,5,5);Chip.alignment=TextAnchor.MiddleCenter;
            goodChip=new GUIStyle(Chip);goodChip.normal.textColor=new Color(.43f,.86f,.66f);
            warnChip=new GUIStyle(Chip);warnChip.normal.textColor=new Color(.97f,.75f,.40f);
            mutedChip=new GUIStyle(Chip);mutedChip.normal.textColor=dim;
            Window=new GUIStyle(Skin.window){padding=new RectOffset(18,18,16,14),border=new RectOffset(6,6,6,6)};
            foreach(var state in new[]{Window.normal,Window.hover,Window.active,Window.focused,Window.onNormal,Window.onHover,Window.onActive,Window.onFocused})state.background=panel;
            Card=new GUIStyle(Skin.box){padding=new RectOffset(14,14,12,12),margin=new RectOffset(0,0,5,5),border=new RectOffset(6,6,6,6)};Card.normal.background=surface;
            Secondary=Button(surface,hover);Primary=Button(blue,blueHover);Danger=Button(red,Texture(new Color(.58f,.24f,.27f)));
            Row=Button(surface,hover);Row.alignment=TextAnchor.MiddleLeft;Row.padding=new RectOffset(12,12,9,9);
            Selected=Button(blue,blueHover);Selected.alignment=TextAnchor.MiddleLeft;Selected.padding=Row.padding;
            Skin.label=Body;Skin.button=Secondary;Skin.box=Card;
            Skin.textField=new GUIStyle(Skin.textField){fontSize=14,padding=new RectOffset(11,11,8,8),border=new RectOffset(6,6,6,6)};
            Skin.textField.normal.background=field;Skin.textField.focused.background=surface;Skin.textField.normal.textColor=ink;Skin.textField.focused.textColor=ink;
            var track=Texture(new Color(.19f,.25f,.32f));var thumb=Texture(cyan);
            Skin.horizontalSlider=new GUIStyle(Skin.horizontalSlider){fixedHeight=7,margin=new RectOffset(0,0,8,8),border=new RectOffset(3,3,3,3)};Skin.horizontalSlider.normal.background=track;
            Skin.horizontalSliderThumb=new GUIStyle(Skin.horizontalSliderThumb){fixedWidth=13,fixedHeight=17,border=new RectOffset(3,3,3,3)};
            Skin.horizontalSliderThumb.normal.background=thumb;Skin.horizontalSliderThumb.hover.background=thumb;Skin.horizontalSliderThumb.active.background=thumb;
            Skin.verticalScrollbar=new GUIStyle(Skin.verticalScrollbar);Skin.verticalScrollbar.normal.background=field;
            Skin.verticalScrollbarThumb=new GUIStyle(Skin.verticalScrollbarThumb){border=new RectOffset(5,5,5,5)};
            Skin.verticalScrollbarThumb.normal.background=hover;Skin.verticalScrollbarThumb.hover.background=blue;Skin.verticalScrollbarThumb.active.background=blueHover;
        }
        private GUIStyle Text(int size,Color color,FontStyle weight=FontStyle.Normal)
        {var style=new GUIStyle(Skin.label){fontSize=size,fontStyle=weight,wordWrap=true,richText=false,padding=new RectOffset(0,0,2,2)};style.normal.textColor=color;return style;}
        private GUIStyle Button(Texture2D normal,Texture2D hover)
        {
            var style=new GUIStyle(Skin.button){fontSize=13,fontStyle=FontStyle.Bold,wordWrap=true,richText=false,padding=new RectOffset(12,12,9,9),margin=new RectOffset(2,2,2,2),border=new RectOffset(6,6,6,6)};
            style.normal.background=normal;style.hover.background=hover;style.active.background=hover;style.focused.background=hover;
            foreach(var state in new[]{style.normal,style.hover,style.active,style.focused})state.textColor=ink;
            return style;
        }
        private Texture2D Texture(Color color)
        {
            const int size=16;var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);texture.hideFlags=HideFlags.HideAndDontSave;texture.filterMode=FilterMode.Bilinear;
            var pixels=new Color[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
            {
                var dx=Mathf.Max(5-x,x-10);var dy=Mathf.Max(5-y,y-10);
                var coverage=dx>0&&dy>0?Mathf.Clamp01(5.5f-Mathf.Sqrt(dx*dx+dy*dy)):1;
                pixels[y*size+x]=new Color(color.r,color.g,color.b,color.a*coverage);
            }
            texture.SetPixels(pixels);texture.Apply();textures.Add(texture);return texture;
        }
        internal void Heading(string title,string subtitle,Action hide)
        {
            GUILayout.BeginHorizontal();GUILayout.BeginVertical();GUILayout.Label(title,Title);GUILayout.Label(subtitle,Subtitle);GUILayout.EndVertical();
            if(GUILayout.Button("Hide",Secondary,GUILayout.Width(62),GUILayout.Height(32)))hide();GUILayout.EndHorizontal();GUILayout.Space(12);
        }
        internal void BeginCard(string section=null){GUILayout.BeginVertical(Card);if(section!=null){GUILayout.Label(section,Section);GUILayout.Space(5);}}
        internal void EndCard(){GUILayout.EndVertical();}
        internal void Badge(string label,string tone="info"){GUILayout.Label(label,tone=="good"?goodChip:tone=="warn"?warnChip:tone=="muted"?mutedChip:Chip,GUILayout.ExpandWidth(false),GUILayout.MaxWidth(200));}
        internal float Slider(string title,float value,float min,float max,string suffix="")
        {
            GUILayout.BeginHorizontal();GUILayout.Label(title,Body);GUILayout.FlexibleSpace();GUILayout.Label(value.ToString("0.0")+suffix,Muted,GUILayout.ExpandWidth(false));GUILayout.EndHorizontal();
            return GUILayout.HorizontalSlider(value,min,max);
        }
        internal bool Switch(string label,bool value)
        {GUILayout.BeginHorizontal();GUILayout.Label(label,Body);if(GUILayout.Button(value?"ON":"OFF",value?Primary:Secondary,GUILayout.Width(60)))value=!value;GUILayout.EndHorizontal();return value;}
        public void Dispose(){foreach(var texture in textures)if(texture)UnityEngine.Object.Destroy(texture);textures.Clear();if(Skin)UnityEngine.Object.Destroy(Skin);}
    }
}
