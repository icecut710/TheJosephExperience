-- ============================================================================
-- Good Job, Joseph! - Supabase setup (idempotent)
--
-- Run this in the Supabase SQL editor (or via the CLI) to create the schema,
-- RLS policies, indexes, and seed rows + storage. Safe to re-run.
--
-- The desktop app talks to PostgREST and Storage using ONLY the anon key with
-- RLS enabled, so it is read-only and can never modify or delete remote data.
-- ============================================================================

-- ----------------------------------------------------------------------------
-- 1. Table (single canonical source of truth)
-- ----------------------------------------------------------------------------
create table if not exists public.celebration_images (
  id            uuid primary key default gen_random_uuid(),
  display_name  text not null,
  storage_path  text not null,
  category      text,
  tags          text[],
  enabled       boolean not null default true,
  favorite      boolean not null default false,
  weight        integer not null default 1,
  sha256        text,
  mime_type     text,
  file_size     bigint,
  width         integer,
  height        integer,
  deleted_at    timestamptz,
  created_at    timestamptz not null default now(),
  updated_at    timestamptz not null default now(),

  -- weight must be >= 1 for weighted selection to behave.
  constraint celebration_images_weight_gte_1 check (weight >= 1)
);

-- ----------------------------------------------------------------------------
-- 2. Idempotent column additions (safe for existing databases)
-- ----------------------------------------------------------------------------
do $$
begin
  if not exists (select 1 from information_schema.columns
                 where table_schema='public' and table_name='celebration_images' and column_name='width') then
    alter table public.celebration_images add column width integer;
  end if;
  if not exists (select 1 from information_schema.columns
                 where table_schema='public' and table_name='celebration_images' and column_name='height') then
    alter table public.celebration_images add column height integer;
  end if;
  if not exists (select 1 from information_schema.columns
                 where table_schema='public' and table_name='celebration_images' and column_name='deleted_at') then
    alter table public.celebration_images add column deleted_at timestamptz;
  end if;
end $$;

comment on table public.celebration_images is
  'Remote canonical library of Joseph images synced to the desktop app.';

-- ----------------------------------------------------------------------------
-- 3. Unique storage path (idempotent)
-- ----------------------------------------------------------------------------
do $$
begin
  if not exists (select 1 from pg_indexes
                 where schemaname='public' and tablename='celebration_images'
                 and indexdef ilike '%storage_path%union%') then
    alter table public.celebration_images add constraint celebration_images_storage_path_key unique (storage_path);
  end if;
exception when others then
  -- constraint/unique already exists under a different name; ignore.
  null;
end $$;

-- ----------------------------------------------------------------------------
-- 4. Updated-at trigger
-- ----------------------------------------------------------------------------
create or replace function public.set_updated_at()
returns trigger language plpgsql as $$
begin
  new.updated_at = now();
  return new;
end;
$$;

drop trigger if exists trg_celebration_images_updated on public.celebration_images;
create trigger trg_celebration_images_updated
  before update on public.celebration_images
  for each row execute function public.set_updated_at();

-- ----------------------------------------------------------------------------
-- 5. Indexes
-- ----------------------------------------------------------------------------
create index if not exists idx_celebration_images_enabled on public.celebration_images (enabled) where enabled = true;
create index if not exists idx_celebration_images_active on public.celebration_images (deleted_at) where deleted_at is null;
create index if not exists idx_celebration_images_category on public.celebration_images (category);
create index if not exists idx_celebration_images_updated on public.celebration_images (updated_at desc);

-- ----------------------------------------------------------------------------
-- 6. Row Level Security (READ ONLY for anon/authenticated)
-- ----------------------------------------------------------------------------
alter table public.celebration_images enable row level security;

-- Public (anon) clients may only SELECT rows that are enabled and not deleted.
drop policy if exists "celebration_images_anon_select" on public.celebration_images;
create policy "celebration_images_anon_select"
  on public.celebration_images
  for select
  using (enabled = true and deleted_at is null);

-- authenticated role: read-only too. No write policies are created, so no
-- client (desktop app included) can modify or delete remote rows.
drop policy if exists "celebration_images_auth_select" on public.celebration_images;
create policy "celebration_images_auth_select"
  on public.celebration_images
  for select
  using (true);

-- ----------------------------------------------------------------------------
-- 7. Storage buckets (must match the client's cloud-config.json / .env)
--    Images bucket  : good-job-joseph-images
--    Audio bucket   : celebration-audio
-- ----------------------------------------------------------------------------
insert into storage.buckets (id, name, public)
values ('good-job-joseph-images', 'good-job-joseph-images', true)
on conflict (id) do nothing;

insert into storage.buckets (id, name, public)
values ('celebration-audio', 'celebration-audio', true)
on conflict (id) do nothing;

-- Public read access to the image bucket objects (read-only).
drop policy if exists "joseph_images_public_read" on storage.objects;
create policy "joseph_images_public_read"
  on storage.objects
  for select
  using (bucket_id = 'good-job-joseph-images');

-- Public read access to the audio bucket objects (read-only).
drop policy if exists "joseph_audio_public_read" on storage.objects;
create policy "joseph_audio_public_read"
  on storage.objects
  for select
  using (bucket_id = 'celebration-audio');

-- ----------------------------------------------------------------------------
-- 8. Example seed (optional)
-- ----------------------------------------------------------------------------
-- Upload your image to the bucket at path `images/seed-joseph.jpg`, then run:
-- insert into public.celebration_images
--   (display_name, storage_path, category, tags, enabled, favorite, weight, sha256, mime_type)
-- values
--   ('Seed Joseph', 'images/seed-joseph.jpg', 'Classic',
--    array['seed','classic','joseph'], true, true, 1,
--    '<computed-sha256-of-the-file>', 'image/jpeg');