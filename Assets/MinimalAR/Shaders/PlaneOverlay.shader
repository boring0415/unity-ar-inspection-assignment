Shader "MinimalAR/PlaneOverlay"
{
    Properties { _Color ("Plane Color", Color) = (0.1, 0.8, 1, 0.25) }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        // 双面半透明，不写入深度，保留相机画面以及立方体的可见性。
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Offset -1, -1
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            fixed4 _Color;
            float4 vert(float4 vertex : POSITION) : SV_POSITION
            {
                return UnityObjectToClipPos(vertex);
            }
            fixed4 frag() : SV_Target { return _Color; }
            ENDCG
        }
    }
}
