import { describe, expect, it } from 'vitest';
import RAPIER from '@dimforge/rapier3d-compat';
import * as THREE from 'three';

describe('toolchain smoke', () => {
  it('runs Rapier headlessly and a body falls under gravity', async () => {
    await RAPIER.init();
    const world = new RAPIER.World({ x: 0, y: -9.81, z: 0 });
    const body = world.createRigidBody(RAPIER.RigidBodyDesc.dynamic().setTranslation(0, 5, 0));
    world.createCollider(RAPIER.ColliderDesc.ball(0.5), body);
    for (let i = 0; i < 30; i++) world.step();
    expect(body.translation().y).toBeLessThan(5);
  });

  it('can build three.js scene graph objects without a WebGL context', () => {
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(1, 1, 1), new THREE.MeshStandardMaterial());
    mesh.position.set(1, 2, 3);
    mesh.updateMatrixWorld();
    expect(mesh.getWorldPosition(new THREE.Vector3()).y).toBe(2);
  });
});
