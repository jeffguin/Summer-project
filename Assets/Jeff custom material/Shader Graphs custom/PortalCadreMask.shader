Shader "Custom/PortalCadreMask"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-1"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "PortalCadreMask"

            ZWrite Off
            ZTest Always
            ColorMask 0

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }
        }
    }
}