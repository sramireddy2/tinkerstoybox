import * as THREE from 'three';
import RAPIER from '@dimforge/rapier3d-compat';

// M0 smoke scene: proves the three.js + Rapier + Vite + Pages pipeline end to end.
// Replaced by the real game bootstrap in M1.
async function boot() {
  await RAPIER.init();

  const app = document.getElementById('app')!;
  const renderer = new THREE.WebGLRenderer({ antialias: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.setSize(window.innerWidth, window.innerHeight);
  renderer.shadowMap.enabled = true;
  app.appendChild(renderer.domElement);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x2a1a5e);
  const camera = new THREE.PerspectiveCamera(60, window.innerWidth / window.innerHeight, 0.1, 200);
  camera.position.set(6, 5, 9);
  camera.lookAt(0, 1, 0);

  scene.add(new THREE.HemisphereLight(0xfff1d6, 0x4b3a8c, 1.2));
  const sun = new THREE.DirectionalLight(0xffffff, 2.2);
  sun.position.set(5, 10, 4);
  sun.castShadow = true;
  scene.add(sun);

  const world = new RAPIER.World({ x: 0, y: -9.81, z: 0 });
  world.createCollider(RAPIER.ColliderDesc.cuboid(10, 0.1, 10));
  const floor = new THREE.Mesh(
    new THREE.BoxGeometry(20, 0.2, 20),
    new THREE.MeshStandardMaterial({ color: 0xffd166 }),
  );
  floor.receiveShadow = true;
  scene.add(floor);

  const palette = [0xef476f, 0x06d6a0, 0x118ab2, 0xffd166, 0xf78c6b, 0x9b5de5];
  const blocks: { body: RAPIER.RigidBody; mesh: THREE.Mesh }[] = [];
  for (let i = 0; i < 24; i++) {
    const s = 0.3 + (i % 5) * 0.12;
    const body = world.createRigidBody(
      RAPIER.RigidBodyDesc.dynamic().setTranslation((i % 4) - 1.5, 3 + i * 0.9, ((i * 7) % 5) * 0.4 - 1),
    );
    world.createCollider(RAPIER.ColliderDesc.cuboid(s, s, s).setRestitution(0.4), body);
    const mesh = new THREE.Mesh(
      new THREE.BoxGeometry(s * 2, s * 2, s * 2),
      new THREE.MeshStandardMaterial({ color: palette[i % palette.length], roughness: 0.35 }),
    );
    mesh.castShadow = true;
    mesh.receiveShadow = true;
    scene.add(mesh);
    blocks.push({ body, mesh });
  }

  window.addEventListener('resize', () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
  });

  renderer.setAnimationLoop(() => {
    world.step();
    for (const { body, mesh } of blocks) {
      const t = body.translation();
      const r = body.rotation();
      mesh.position.set(t.x, t.y, t.z);
      mesh.quaternion.set(r.x, r.y, r.z, r.w);
    }
    renderer.render(scene, camera);
  });
}

boot();
