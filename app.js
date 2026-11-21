(() => {
  'use strict';
  const root = document.documentElement;
  root.classList.add('js');
  const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
  const motionButton = document.querySelector('.motion-control');
  let motionPaused = reducedMotion.matches;
  let explicitMotionChoice = false;
  let petTimeout;
  let petCount = 0;

  function updateMotion() {
    root.classList.toggle('motion-paused', motionPaused || document.hidden);
    root.classList.toggle('motion-enabled', explicitMotionChoice && !motionPaused);
    motionButton.setAttribute('aria-pressed', String(motionPaused));
    motionButton.querySelector('span').textContent = motionPaused ? 'Відновити анімації' : 'Зупинити анімації';
    motionButton.querySelector('use').setAttribute('href', motionPaused ? '#i-play' : '#i-pause');
  }
  motionButton.addEventListener('click', () => {
    explicitMotionChoice = true;
    motionPaused = !motionPaused;
    updateMotion();
  });
  reducedMotion.addEventListener('change', event => {
    if (!explicitMotionChoice) motionPaused = event.matches;
    updateMotion();
  });
  document.addEventListener('visibilitychange', updateMotion);
  updateMotion();

  const reveals = document.querySelectorAll('.reveal');
  if ('IntersectionObserver' in window) {
    const observer = new IntersectionObserver(entries => {
      entries.forEach(entry => {
        if (entry.isIntersecting) {
          entry.target.classList.add('is-visible');
          observer.unobserve(entry.target);
        }
      });
    }, { threshold: 0.08, rootMargin: '0px 0px 25px 0px' });
    reveals.forEach(element => observer.observe(element));
  } else reveals.forEach(element => element.classList.add('is-visible'));

  function createBars(selector, count) {
    const container = document.querySelector(selector);
    for (let i = 0; i < count; i++) {
      const bar = document.createElement('i');
      bar.style.setProperty('--bar-height', `${7 + ((i * 13 + 5) % 21)}px`);
      bar.style.setProperty('--duration', `${0.45 + (i % 5) * 0.12}s`);
      bar.style.setProperty('--delay', `${-(i * 0.17)}s`);
      container.append(bar);
    }
  }
  createBars('.demo-wave', 25);
  createBars('.equalizer', 18);

  const island = document.querySelector('.demo-island');
  const desktop = document.querySelector('.demo-desktop');
  const modeLabel = document.querySelector('.island-mode-label');
  const reaction = document.querySelector('.demo-reaction');
  const modes = {
    eyes: ['Радий тебе бачити', 'Стежить за курсором, моргає й відповідає на дотик. У нього є настрій.'],
    music: ['У твоєму ритмі', 'Трек, керування й живі смуги в одному місці. Тут відтворюється лише анімація.'],
    focus: ['Твій час', 'Запусти демонстраційний таймер. Його можна призупинити й продовжити.'],
    devices: ['Завжди на зв’язку', 'Підключив навушники — назва й заряд одразу поруч. Тут показані прикладові 72%.']
  };
  document.querySelectorAll('[data-mode-button]').forEach(button => {
    button.addEventListener('click', () => {
      const mode = button.dataset.modeButton;
      if (!Object.hasOwn(modes, mode)) return;
      island.dataset.mode = mode;
      desktop.dataset.mode = mode;
      document.querySelectorAll('[data-pane]').forEach(pane => { pane.hidden = pane.dataset.pane !== mode; });
      document.querySelectorAll('[data-mode-button]').forEach(item => {
        const selected = item === button;
        item.classList.toggle('is-active', selected);
        item.setAttribute('aria-pressed', String(selected));
      });
      modeLabel.textContent = modes[mode][0];
      document.getElementById('mode-description').textContent = modes[mode][1];
      reaction.textContent = '';
    });
  });

  document.querySelector('.pet-button').addEventListener('click', () => {
    const messages = ['О, це приємно. Давай дружити ♡', 'Уже впізнаю тебе. Привіт!', 'Маленький острів, великий настрій ✧'];
    reaction.textContent = messages[petCount++ % messages.length];
    island.classList.add('pet-happy');
    clearTimeout(petTimeout);
    petTimeout = setTimeout(() => island.classList.remove('pet-happy'), 1600);
  });

  const player = document.querySelector('.player-toggle');
  player.addEventListener('click', () => {
    const paused = island.classList.toggle('music-paused');
    player.setAttribute('aria-pressed', String(paused));
    player.setAttribute('aria-label', paused ? 'Відновити демоанімацію музики' : 'Призупинити демоанімацію музики');
    player.querySelector('use').setAttribute('href', paused ? '#i-play' : '#i-pause');
  });

  let focusRemaining = 25 * 60;
  let focusDeadline = 0;
  let focusInterval;
  const focusButton = document.querySelector('.focus-toggle');
  const focusClock = document.querySelector('.focus-clock');
  const focusCaption = document.querySelector('.focus-caption');
  function renderFocus() {
    if (focusDeadline) focusRemaining = Math.max(0, Math.ceil((focusDeadline - Date.now()) / 1000));
    focusClock.textContent = `${Math.floor(focusRemaining / 60).toString().padStart(2, '0')}:${(focusRemaining % 60).toString().padStart(2, '0')}`;
    document.querySelector('.focus-track > span').style.width = `${(1 - focusRemaining / 1500) * 100}%`;
    if (focusRemaining === 0) {
      clearInterval(focusInterval);
      focusDeadline = 0;
      focusCaption.textContent = 'Час перепочити. Ти молодець!';
      focusButton.setAttribute('aria-label', 'Почати демонстраційний таймер знову');
      focusButton.querySelector('use').setAttribute('href', '#i-play');
    }
  }
  focusButton.addEventListener('click', () => {
    if (focusDeadline) {
      renderFocus();
      clearInterval(focusInterval);
      focusDeadline = 0;
      focusCaption.textContent = 'Пауза. Видихни.';
      focusButton.setAttribute('aria-label', 'Продовжити демонстраційний таймер');
      focusButton.querySelector('use').setAttribute('href', '#i-play');
    } else {
      if (focusRemaining <= 0) focusRemaining = 1500;
      focusDeadline = Date.now() + focusRemaining * 1000;
      focusInterval = setInterval(renderFocus, 250);
      focusCaption.textContent = 'Ти тут. Решта зачекає.';
      focusButton.setAttribute('aria-label', 'Призупинити демонстраційний таймер');
      focusButton.querySelector('use').setAttribute('href', '#i-pause');
      renderFocus();
    }
  });

  document.querySelectorAll('.swatch').forEach(button => {
    button.addEventListener('click', () => {
      root.style.setProperty('--accent', button.dataset.color);
      document.querySelectorAll('.swatch').forEach(item => {
        item.classList.toggle('is-selected', item === button);
        item.setAttribute('aria-pressed', String(item === button));
      });
    });
  });

  let pointerFrame = 0;
  let pointerX = 0;
  let pointerY = 0;
  const pupils = [...document.querySelectorAll('.eye i')];
  document.addEventListener('pointermove', event => {
    if (motionPaused || event.pointerType === 'touch') return;
    pointerX = event.clientX; pointerY = event.clientY;
    if (pointerFrame) return;
    pointerFrame = requestAnimationFrame(() => {
      pupils.forEach(pupil => {
        const bounds = pupil.parentElement.getBoundingClientRect();
        const x = Math.max(-2.5, Math.min(2.5, (pointerX - bounds.x - bounds.width / 2) / 150));
        const y = Math.max(-2, Math.min(2, (pointerY - bounds.y - bounds.height / 2) / 180));
        pupil.style.setProperty('--look-x', `${x}px`);
        pupil.style.setProperty('--look-y', `${y}px`);
      });
      pointerFrame = 0;
    });
  }, { passive: true });
})();
