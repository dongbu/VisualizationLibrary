/**************************************************************************************/
/*                                                                                    */
/*  Copyright (c) 2005-2017, Michele Bosi.                                            */
/*  All rights reserved.                                                              */
/*                                                                                    */
/*  This file is part of Visualization Library                                        */
/*  http://visualizationlibrary.org                                                   */
/*                                                                                    */
/*  Released under the OSI approved Simplified BSD License                            */
/*  http://www.opensource.org/licenses/bsd-license.php                                */
/*                                                                                    */
/**************************************************************************************/

/* raycast isosurface, transparent */

#version 330 core

struct vl_LightParameters {
  vec4 position;
  vec4 diffuse;
  vec4 specular;
  vec4 ambient;
  bool enabled;
};

in vec3 frag_position; // in object space
in vec4 tex_coord;
out vec4 frag_output;  // fragment shader output

uniform vl_LightParameters lights[4];

uniform sampler3D volume_texunit;
uniform sampler3D gradient_texunit;
uniform sampler1D trfunc_texunit;
uniform vec3 eye_position;         // camera position in object space
// uniform vec3 eye_look;          // camera look direction in object space
uniform float sample_step;         // step used to advance the sampling ray: a good value is: `1/4 / <tex-dim>` or `1/8 / <tex-dim>`.
uniform float val_threshold;
uniform bool precomputed_gradient; // whether the gradient has been precomputed or not
uniform vec3 gradient_delta;       // for on-the-fly gradient computation: a good value is `1/4 / <tex-dim>`.
uniform vec3 texel_centering;      // normalized x/y/z offeset required to center on a texel

// Computes a simplified lighting equation
vec3 blinn( vec3 N, vec3 V, vec3 L, int light, vec3 diffuse )
{
  // material properties
  // you might want to put this into a bunch or uniforms
  vec3 Ka = vec3( 1.0, 1.0, 1.0 );
  vec3 Kd = diffuse; // vec3( 1.0, 1.0, 1.0 );
  vec3 Ks = vec3( 0.5, 0.5, 0.5 );
  float shininess = 100.0;

  // diffuse coefficient
  float diff_coeff = max( dot( L, N ), 0.0 );

  // specular coefficient
  vec3 H = normalize( L + V );
  float spec_coeff = diff_coeff > 0.0 ? pow( max( dot( H, N ), 0.0 ), shininess ) : 0.0;

  // final lighting model
  return  Ka * lights[light].ambient.rgb +
          Kd * lights[light].diffuse.rgb  * diff_coeff +
          Ks * lights[light].specular.rgb * spec_coeff ;
}

vec4 computeFragColor( vec3 iso_pos )
{
  // compute lighting at isosurface point

  // compute the gradient and lighting only if the pixel is visible "enough"
  vec3 N;
  if ( precomputed_gradient )
  {
    // retrieve pre-computed gradient
    N  = normalize( ( texture( gradient_texunit, iso_pos ).xyz - vec3( 0.5, 0.5, 0.5 ) ) * 2.0 );
  }
  else
  {
    // on-the-fly gradient computation: slower but requires less memory (no gradient texture required).
    vec3 a, b;
    a.x = texture( volume_texunit, iso_pos - vec3( gradient_delta.x, 0.0, 0.0 ) ).r;
    b.x = texture( volume_texunit, iso_pos + vec3( gradient_delta.x, 0.0, 0.0 ) ).r;
    a.y = texture( volume_texunit, iso_pos - vec3( 0.0, gradient_delta.y, 0.0 ) ).r;
    b.y = texture( volume_texunit, iso_pos + vec3( 0.0, gradient_delta.y, 0.0 ) ).r;
    a.z = texture( volume_texunit, iso_pos - vec3( 0.0, 0.0, gradient_delta.z ) ).r;
    b.z = texture( volume_texunit, iso_pos + vec3( 0.0, 0.0, gradient_delta.z ) ).r;
    N  = normalize( a - b );
  }

  vec3 V  = normalize( eye_position - frag_position );
  vec4 diffuse = texture( trfunc_texunit, val_threshold );
  vec3 final_color = vec3( 0, 0, 0 );
  for( int i = 0; i < 4; ++i )
  {
    if ( lights[i].enabled )
    {
      vec3 L = normalize( lights[i].position.xyz - frag_position );
      // double sided lighting
      if ( dot( L, N ) < 0.0 ) {
          N = -N;
      }

      final_color = final_color + blinn( N, V, L, i, diffuse.rgb );
    }
  }

  return vec4( final_color, diffuse.a );
}

const float ALPHA = 0.50;

void main(void)
{
  // NOTE:
  // 1) Ray direction goes from eye_position to frag_position, i.e. front to back
  // 2) We assume the volume has a cube-like shape. To support non cubic volumes `sample_step` should be a vec3.
  vec3 ray_dir = normalize( frag_position - eye_position );
  vec3 ray_step = ray_dir * sample_step;
  vec3 ray_pos = tex_coord.xyz; // the current ray position

  // NOTE:
  // These are not adjusted tex coords, for better precision we should use
  // the same values generated by RaycastVolume::generateTextureCoordinates
  vec3 pos111 = vec3( 1.0, 1.0, 1.0 ) - texel_centering;
  vec3 pos000 = vec3( 0.0, 0.0, 0.0 ) + texel_centering;

  float val = texture(volume_texunit, tex_coord.xyz ).r;
  bool sign_prev = val > val_threshold;
  bool isosurface_found = false;
  float transmittance = 1.0;
  frag_output.rgb = vec3( 0.0 );
  do
  {
    ray_pos += ray_step;

    // Leave if end of cube
    if ( any( greaterThan( ray_pos, pos111 ) ) || any( lessThan( ray_pos, pos000 ) ) ) {
      break;
    }

    val = texture( volume_texunit, ray_pos ).r;
    bool sign_cur = val > val_threshold;
    if ( sign_cur != sign_prev )
    {
      sign_prev = sign_cur;
      vec3 iso_pos = ray_pos - ray_step * 0.5;
      vec4 rgba = computeFragColor( iso_pos );
      float alpha = rgba.a * ALPHA;
      frag_output.rgb += rgba.rgb * transmittance;
      transmittance *= ( 1.0 - alpha );
      frag_output.a = ( 1.0 - transmittance );
      isosurface_found = true;
      if ( frag_output.a > 0.996 ) {
                break;
      }
    }
  }
  while(true);

  if ( ! isosurface_found ) {
    discard;
  }
}

// Have fun!
