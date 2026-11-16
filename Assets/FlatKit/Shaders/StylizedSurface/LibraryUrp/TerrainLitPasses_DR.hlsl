#ifndef FLATKIT_TERRAIN_LIT_PASSES_DR_INCLUDED
#define FLATKIT_TERRAIN_LIT_PASSES_DR_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/Shaders/Terrain/TerrainLitPasses.hlsl"
#include "LibraryUrp/StylizedInput.hlsl"

void ComputeMasks_DSTRM(out half4 masks[4], half4 hasMask, float4 uvSplat01, float4 uvSplat23)
{
    masks[0] = 0.5h;
    masks[1] = 0.5h;
    masks[2] = 0.5h;
    masks[3] = 0.5h;

#ifdef _MASKMAP
    masks[0] = lerp(masks[0], SAMPLE_TEXTURE2D(_Mask0, sampler_Mask0, uvSplat01.xy), hasMask.x);
    masks[1] = lerp(masks[1], SAMPLE_TEXTURE2D(_Mask1, sampler_Mask0, uvSplat01.zw), hasMask.y);
    masks[2] = lerp(masks[2], SAMPLE_TEXTURE2D(_Mask2, sampler_Mask0, uvSplat23.xy), hasMask.z);
    masks[3] = lerp(masks[3], SAMPLE_TEXTURE2D(_Mask3, sampler_Mask0, uvSplat23.zw), hasMask.w);
#endif

    masks[0] *= _MaskMapRemapScale0.rgba;
    masks[0] += _MaskMapRemapOffset0.rgba;
    masks[1] *= _MaskMapRemapScale1.rgba;
    masks[1] += _MaskMapRemapOffset1.rgba;
    masks[2] *= _MaskMapRemapScale2.rgba;
    masks[2] += _MaskMapRemapOffset2.rgba;
    masks[3] *= _MaskMapRemapScale3.rgba;
    masks[3] += _MaskMapRemapOffset3.rgba;
}

#ifdef TERRAIN_GBUFFER
FragmentOutput SplatmapFragment_DSTRM(Varyings IN)
#else
half4 SplatmapFragment_DSTRM(Varyings IN) : SV_TARGET
#endif
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
#ifdef _ALPHATEST_ON
    ClipHoles(IN.uvMainAndLM.xy);
#endif

    half3 normalTS = half3(0.0h, 0.0h, 1.0h);
#ifdef TERRAIN_SPLAT_BASEPASS
    half3 albedo = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uvMainAndLM.xy).rgb;
    half smoothness = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uvMainAndLM.xy).a;
    half metallic = SAMPLE_TEXTURE2D(_MetallicTex, sampler_MetallicTex, IN.uvMainAndLM.xy).r;
    half alpha = 1;
    half occlusion = 1;
#else

    half4 hasMask = half4(_LayerHasMask0, _LayerHasMask1, _LayerHasMask2, _LayerHasMask3);
    half4 masks[4];
    ComputeMasks_DSTRM(masks, hasMask, IN.uvSplat01, IN.uvSplat23);

    float2 splatUV = (IN.uvMainAndLM.xy * (_Control_TexelSize.zw - 1.0f) + 0.5f) * _Control_TexelSize.xy;
    half4 splatControl = SAMPLE_TEXTURE2D(_Control, sampler_Control, splatUV);

#ifdef _TERRAIN_BLEND_HEIGHT
    // disable Height Based blend when there are more than 4 layers (multi-pass breaks the normalization)
    if (_NumLayersCount <= 4)
        HeightBasedSplatModify(splatControl, masks);
#endif

    half weight;
    half4 mixedDiffuse;
    half4 defaultSmoothness;
    SplatmapMix(IN.uvMainAndLM, IN.uvSplat01, IN.uvSplat23, splatControl, weight, mixedDiffuse, defaultSmoothness, normalTS);
    half3 albedo = mixedDiffuse.rgb;

    half4 defaultMetallic = half4(_Metallic0, _Metallic1, _Metallic2, _Metallic3);
    half4 defaultOcclusion = half4(_MaskMapRemapScale0.g, _MaskMapRemapScale1.g, _MaskMapRemapScale2.g, _MaskMapRemapScale3.g) +
                            half4(_MaskMapRemapOffset0.g, _MaskMapRemapOffset1.g, _MaskMapRemapOffset2.g, _MaskMapRemapOffset3.g);

    half4 maskSmoothness = half4(masks[0].a, masks[1].a, masks[2].a, masks[3].a);
    defaultSmoothness = lerp(defaultSmoothness, maskSmoothness, hasMask);
    half smoothness = dot(splatControl, defaultSmoothness);

    half4 maskMetallic = half4(masks[0].r, masks[1].r, masks[2].r, masks[3].r);
    defaultMetallic = lerp(defaultMetallic, maskMetallic, hasMask);
    half metallic = dot(splatControl, defaultMetallic);

    half4 maskOcclusion = half4(masks[0].g, masks[1].g, masks[2].g, masks[3].g);
    defaultOcclusion = lerp(defaultOcclusion, maskOcclusion, hasMask);
    half occlusion = dot(splatControl, defaultOcclusion);
    half alpha = weight;
#endif

    InputData inputData;
    InitializeInputData(IN, normalTS, inputData);

    // If lightmap is enabled, flip the normal if the lightmap is flipped.
    /*
#if defined(LIGHTMAP_ON)
    const half lightmapWorkaroundFlip = -1.0;
    inputData.normalWS = lightmapWorkaroundFlip * inputData.normalWS;
#endif
    */

#if UNITY_VERSION > 202340
    SETUP_DEBUG_TEXTURE_DATA_FOR_TEX(inputData, IN.uvMainAndLM.xy, _BaseMap);
#elif VERSION_GREATER_EQUAL(12, 1)
    SETUP_DEBUG_TEXTURE_DATA(inputData, IN.uvMainAndLM.xy, _BaseMap);
#endif

#if defined(_DBUFFER)
    half3 specular = half3(0.0h, 0.0h, 0.0h);
    ApplyDecal(IN.clipPos,
        albedo,
        specular,
        inputData.normalWS,
        metallic,
        occlusion,
        smoothness);
#endif

#if UNITY_VERSION > 60000012 // Minor version not precise, somewhere between 6.0.4 and 6.0.23.
    InitializeBakedGIData(IN, inputData);
#endif

#ifdef TERRAIN_GBUFFER

    BRDFData brdfData;
    InitializeBRDFData(albedo, metallic, /* specular */ half3(0.0h, 0.0h, 0.0h), smoothness, alpha, brdfData);

    half4 color;
    color.rgb = GlobalIllumination(brdfData, inputData.bakedGI, occlusion, inputData.normalWS, inputData.viewDirectionWS);
    color.a = alpha;

    SplatmapFinalColor(color, inputData.fogCoord);

    return BRDFDataToGbuffer(brdfData, inputData, smoothness, color.rgb);

#else
    {
        _BaseColor.rgb *= albedo;

        #ifdef _CELPRIMARYMODE_SINGLE
        _ColorDim.rgb *= albedo;
        #endif

        #ifdef _CELPRIMARYMODE_STEPS
        _ColorDimSteps.rgb *= albedo;
        #endif
        
        #ifdef _CELPRIMARYMODE_CURVE
        _ColorDimCurve.rgb *= albedo;
        #endif
        
        #ifdef DR_CEL_EXTRA_ON
        _ColorDimExtra.rgb *= albedo;
        #endif
        
        #ifdef DR_GRADIENT_ON
        _ColorGradient.rgb *= albedo;
        #endif
    }

    SurfaceData surfaceData;
    surfaceData.albedo = albedo;
    surfaceData.alpha = alpha;
    surfaceData.emission = 0;
    surfaceData.specular = 0;
    surfaceData.smoothness = smoothness;
    surfaceData.metallic = metallic;
    surfaceData.occlusion = occlusion;
    surfaceData.normalTS = normalTS;
    surfaceData.clearCoatMask = 0;
    surfaceData.clearCoatSmoothness = 0;

    half4 color = UniversalFragment_DSTRM(inputData, surfaceData, IN.uvMainAndLM.xy);

    SplatmapFinalColor(color, inputData.fogCoord);

    return half4(color.rgb, 1.0h);
#endif
}

#endif  // FLATKIT_TERRAIN_LIT_PASSES_DR_INCLUDED
