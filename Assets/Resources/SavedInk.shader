Shader "Shodo/SavedInk"
{
    Properties { _Color ("Ink color", Color) = (0,0,0,1) }
    SubShader
    {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" }
        // Dense strokes can reverse triangle winding at tight turns. Both faces are ink.
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            struct Input { float4 vertex : POSITION; fixed4 color : COLOR; };
            struct Output { float4 position : SV_POSITION; fixed4 color : COLOR; };
            Output vert(Input input)
            {
                Output output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.color = input.color * _Color;
                return output;
            }
            fixed4 frag(Output input) : SV_Target { return input.color; }
            ENDCG
        }
    }
}
