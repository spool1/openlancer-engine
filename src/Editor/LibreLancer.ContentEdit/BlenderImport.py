import bpy

GLTF_FILE={0}

bpy.ops.export_scene.gltf(filepath=GLTF_FILE,
                          export_format='GLB',
                          check_existing=False,
                          filter_glob='',
                          export_extras=True,
                          use_mesh_edges=True,
                          export_apply=True,
                          export_merge_animation='NLA_TRACK',
                          export_image_format='AUTO')
